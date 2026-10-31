<?php

namespace App\Models;

use App\Notifications\SiteActivity;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Facades\Notification;

/**
 * A user following a mod, vehicle or save for its new releases.
 *
 * @property int $id
 * @property int $user_id
 * @property 'mod'|'vehicle'|'save' $kind
 * @property string $listing_id
 * @property-read User $user
 */
class Follow extends Model
{
    public const KINDS = ['mod', 'vehicle', 'save'];

    protected $guarded = [];

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * Tells everyone who follows the listing about its new release, in the bell. The email digest
     * gathers these later.
     *
     * @param  'mod'|'vehicle'|'save'  $kind
     */
    public static function announce(string $kind, string $listingId, string $name, string $version, string $url, ?string $icon = null, ?int $except = null): void
    {
        $followers = User::whereIn('id', self::where('kind', $kind)->where('listing_id', $listingId)->select('user_id'))
            ->when($except !== null, fn ($query) => $query->whereKeyNot($except))
            ->get();

        Notification::send($followers, new SiteActivity('release', $name, $url, $version, $icon));
    }
}
