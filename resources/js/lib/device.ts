// Phones and tablets, from the browser's user agent. Pass the one from the page props, not navigator's:
// a server-side render runs in Node, whose navigator says "Node.js".
// An iPad asking for the desktop site reports itself as a Mac, so it counts as a desktop.
export function isMobileDevice(userAgent: string): boolean {
    return /Android|iPhone|iPad|iPod|Mobi/.test(userAgent);
}
