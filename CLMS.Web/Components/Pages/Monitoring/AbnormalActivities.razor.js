export function timeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

export function formatTimes(root) {
    if (!root?.isConnected) return;
    for (const element of root.querySelectorAll('time[datetime]')) {
        const date = new Date(element.dateTime);
        if (!Number.isNaN(date.getTime())) element.textContent = date.toLocaleString();
    }
}
