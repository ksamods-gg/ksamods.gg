<?php

namespace App\Models;

use App\Support\ContentIndex;
use Illuminate\Database\Eloquent\Model;

/**
 * The newest version of a followed mod the site has seen, so the next one counts as new.
 *
 * @property string $listing_id
 * @property string $version
 *
 * @phpstan-import-type Listing from ContentIndex
 */
class SeenRelease extends Model
{
    protected $primaryKey = 'listing_id';

    public $incrementing = false;

    protected $keyType = 'string';

    protected $guarded = [];

    /**
     * The version of the listing's newest release, by release date.
     *
     * @param  Listing|array{}  $listing
     */
    public static function newestVersion(array $listing): ?string
    {
        return collect($listing['releases'] ?? [])->sortByDesc('release_date')->first()['version'] ?? null;
    }
}
