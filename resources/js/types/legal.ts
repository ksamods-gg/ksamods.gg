/** Who runs the site, from config/legal.php; null where the environment leaves it unset. */
export type LegalOperator = {
    operator: string | null;
    email: string | null;
    country: string | null;
};
