import Foundation

/// `ModelFileTransport` over a URL session download task: the file is written to disk as it arrives,
/// never held in memory, and the task says how many bytes it has written.
///
/// Every file gets a session of its own: ephemeral, no cookies, no cache. Redirects are followed,
/// because the model host hands a download on to its storage, but only to an `https` address. Any
/// answer but 200 is a failure, so an error page is never taken for a model file.
struct URLSessionModelFileTransport: ModelFileTransport {
    init() {}

    func download(_ url: URL, to destination: URL, received: @escaping @Sendable (Int64) -> Void) async throws {
        try Task.checkCancellation()

        let delegate = ModelDownloadDelegate(destination: destination, received: received)
        let session = URLSession(
            configuration: URLSessionModelFileTransport.makeConfiguration(),
            delegate: delegate,
            delegateQueue: nil
        )
        // A session keeps its delegate until it is invalidated.
        defer { session.finishTasksAndInvalidate() }

        let task = session.downloadTask(with: url)
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                delegate.begin(continuation)
                task.resume()
            }
        } onCancel: {
            task.cancel()
        }
    }

    /// The longest silence of a connection before the download fails, in seconds.
    static let idleTimeoutSeconds: TimeInterval = 60

    /// Ephemeral: no response cache, no cookie storage, cookies never sent.
    static func makeConfiguration() -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.httpCookieAcceptPolicy = .never
        configuration.timeoutIntervalForRequest = idleTimeoutSeconds
        return configuration
    }
}

/// The delegate of one download. The session calls it on its own queue; the lock guards what the
/// waiting caller and the session both touch.
private final class ModelDownloadDelegate: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    private let destination: URL
    private let received: @Sendable (Int64) -> Void
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Void, any Error>?
    /// Why the file that arrived was not kept.
    private var failure: (any Error)?
    private var isStored = false

    init(destination: URL, received: @escaping @Sendable (Int64) -> Void) {
        self.destination = destination
        self.received = received
    }

    func begin(_ continuation: CheckedContinuation<Void, any Error>) {
        lock.withLock { () -> Void in
            self.continuation = continuation
        }
    }

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didWriteData bytesWritten: Int64,
        totalBytesWritten: Int64,
        totalBytesExpectedToWrite: Int64
    ) {
        received(totalBytesWritten)
    }

    /// The system removes its file as soon as this returns, so the file is moved here and now.
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        var problem: (any Error)?
        if let status = (downloadTask.response as? HTTPURLResponse)?.statusCode, status != 200 {
            problem = ModelDownloadFailure.server(status: status)
        } else {
            do {
                try FileManager.default.moveItem(at: location, to: destination)
            } catch {
                problem = error
            }
        }
        let found = problem
        lock.withLock { () -> Void in
            failure = found
            isStored = found == nil
        }
    }

    /// Called once per task, after the file was handed over or instead of it.
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        let state = lock.withLock { () -> (CheckedContinuation<Void, any Error>?, (any Error)?, Bool) in
            let waiting = continuation
            continuation = nil
            return (waiting, failure, isStored)
        }
        guard let waiting = state.0 else { return }

        if let error {
            if (error as? URLError)?.code == .cancelled {
                waiting.resume(throwing: CancellationError())
            } else {
                waiting.resume(throwing: error)
            }
        } else if let failure = state.1 {
            waiting.resume(throwing: failure)
        } else if !state.2 {
            waiting.resume(throwing: ModelDownloadFailure.incomplete)
        } else {
            waiting.resume()
        }
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        completionHandler(request.url?.scheme?.lowercased() == "https" ? request : nil)
    }
}
