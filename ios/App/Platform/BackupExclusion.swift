import Foundation

enum BackupExclusion {
    /// Sets `isExcludedFromBackup` on the item at `url`; failures are ignored.
    ///
    /// The flag belongs to the item on disk, not to its path: a file that is replaced by an atomic write
    /// is a new item, so the caller sets the flag again after every such write.
    static func exclude(_ url: URL) {
        var item = url
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        try? item.setResourceValues(values)
    }
}
