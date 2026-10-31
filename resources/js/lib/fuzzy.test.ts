import { describe, expect, it } from 'vite-plus/test';
import { editDistance, fuzzyScore } from './fuzzy';

describe('editDistance', () => {
    it('counts a missing, extra, wrong or swapped letter as one slip', () => {
        expect(editDistance('rocket', 'roket', 2)).toBe(1);
        expect(editDistance('rocket', 'rockett', 2)).toBe(1);
        expect(editDistance('rocket', 'racket', 2)).toBe(1);
        expect(editDistance('rocket', 'rokcet', 2)).toBe(1);
    });

    it('stops once past the limit', () => {
        expect(editDistance('rocket', 'banana', 1)).toBe(2);
    });
});

describe('fuzzyScore', () => {
    it('finds words by their start, best', () => {
        expect(fuzzyScore('kitten eng', 'Kitten Engineer Redux')).toBe(1);
    });

    it('finds words with typing slips, for less', () => {
        const score = fuzzyScore('rocet', 'Bigger Rockets');

        expect(score).toBeGreaterThan(0);
        expect(score).toBeLessThan(1);
        expect(fuzzyScore('enigneer', 'Kitten Engineer')).toBeGreaterThan(0);
    });

    it('needs every word, and no slips in short words', () => {
        expect(fuzzyScore('rocket fuel', 'Bigger Rockets')).toBe(0);
        expect(fuzzyScore('car', 'Cat tools')).toBe(0);
    });
});
