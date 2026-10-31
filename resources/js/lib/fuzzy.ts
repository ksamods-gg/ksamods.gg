// Typo-tolerant matching for searches: a query word matches a word of the text when it is a
// prefix of it, or a few typing slips away from it (a letter missing, extra, wrong or swapped).

// The slips a word of this length may have: none for short words, where one slip is a
// different word, then one, then two.
const allowedSlips = (length: number) => (length < 4 ? 0 : length < 8 ? 1 : 2);

// Optimal string alignment distance, stopping early once it passes `max`.
export function editDistance(a: string, b: string, max: number): number {
    if (Math.abs(a.length - b.length) > max) {
        return max + 1;
    }

    let previous = Array.from({ length: b.length + 1 }, (_, j) => j);
    let beforePrevious: number[] = [];

    for (let i = 1; i <= a.length; i++) {
        const current = [i];
        let rowMin = i;

        for (let j = 1; j <= b.length; j++) {
            const cost = a[i - 1] === b[j - 1] ? 0 : 1;
            let value = Math.min(
                previous[j] + 1,
                current[j - 1] + 1,
                previous[j - 1] + cost,
            );

            if (
                i > 1 &&
                j > 1 &&
                a[i - 1] === b[j - 2] &&
                a[i - 2] === b[j - 1]
            ) {
                value = Math.min(value, beforePrevious[j - 2] + 1);
            }

            current[j] = value;
            rowMin = Math.min(rowMin, value);
        }

        if (rowMin > max) {
            return max + 1;
        }

        beforePrevious = previous;
        previous = current;
    }

    return previous[b.length];
}

const words = (text: string) =>
    text
        .toLowerCase()
        .split(/[^\p{L}\p{N}]+/u)
        .filter(Boolean);

/**
 * How well every word of the query is found in the text, from 0 (some word is missing) to 1
 * (every word starts a word of the text). A word found only with typing slips scores less.
 */
export function fuzzyScore(query: string, text: string): number {
    const wanted = words(query);
    const have = words(text);

    if (wanted.length === 0) {
        return 0;
    }

    let total = 0;

    for (const word of wanted) {
        let best = 0;

        for (const candidate of have) {
            if (candidate.startsWith(word)) {
                best = 1;
                break;
            }

            const slips = allowedSlips(word.length);

            if (slips > 0) {
                // Against the candidate's start too, a letter shorter or longer, so a slip
                // in a prefix still counts: "rocet" for "rockets".
                const distance = Math.min(
                    editDistance(word, candidate, slips),
                    ...[-1, 0, 1].map((extra) =>
                        editDistance(
                            word,
                            candidate.slice(0, word.length + extra),
                            slips,
                        ),
                    ),
                );

                if (distance <= slips) {
                    best = Math.max(best, 0.6 - distance * 0.1);
                }
            }
        }

        if (best === 0) {
            return 0;
        }

        total += best;
    }

    return total / wanted.length;
}
