export function upcoming<T>(items: T[], time: (item: T) => string, now: number, hours: number): T[] {
    return items.map(item => ({ item, at: Date.parse(time(item)) }))
        .filter(({ at }) => at > now && at <= now + hours * 3600000)
        .sort((a, b) => a.at - b.at).map(({ item }) => item);
}
