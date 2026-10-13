import SwiftUI

extension View {
    /// Reports the height of the container this screen is shown in, safe areas included: the "usable
    /// height" that the compact rules of Home and the live session are written against (screens 2).
    func onContainerHeightChange(_ action: @escaping (CGFloat) -> Void) -> some View {
        onGeometryChange(for: CGFloat.self) { proxy in
            proxy.size.height + proxy.safeAreaInsets.top + proxy.safeAreaInsets.bottom
        } action: { height in
            action(height)
        }
    }
}

extension Theme {
    /// True for a measured container that is lower than the compact threshold. A height of zero means
    /// "not measured yet", which is not compact.
    func isCompactHeight(_ containerHeight: CGFloat) -> Bool {
        containerHeight > 0 && containerHeight < size.compactHeightThreshold
    }
}
