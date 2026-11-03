<?php

namespace App\Models;

use App\Events\ShareDownloadsChanged;
use App\Notifications\SiteActivity;
use Database\Factories\ShareFactory;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Support\Carbon;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;

/**
 * A vehicle or save listing on ksamods.gg as a share host (RFC 0094).
 *
 * @property int $id
 * @property 'vehicle'|'save' $type
 * @property string $listing_id
 * @property int|null $user_id
 * @property string $account
 * @property string $name
 * @property list<string> $authors
 * @property string $abstract
 * @property string|null $description
 * @property string $license
 * @property list<string> $tags
 * @property list<array{id: string, kind: string, min?: string, max?: string}> $dependencies
 * @property string|null $pull_request_url
 * @property 'open'|'merged'|'closed'|null $pull_request_state
 * @property string|null $branch
 * @property int $downloads
 * @property int $published_downloads
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read User|null $user
 * @property array{url: string, sha256: string, width: int, height: int, size: int}|null $icon
 * @property-read Collection<int, ShareRelease> $releases
 * @property-read Collection<int, ShareImage> $images
 */
class Share extends Model
{
    /** @use HasFactory<ShareFactory> */
    use HasFactory;

    protected $guarded = [];

    /**
     * The folder each type's listings live in, in the authored repository and in URLs.
     */
    public const FOLDERS = ['vehicle' => 'vehicles', 'save' => 'saves'];

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * Oldest first, the order of the release list.
     *
     * @return HasMany<ShareRelease, $this>
     */
    public function releases(): HasMany
    {
        return $this->hasMany(ShareRelease::class)->oldest('id');
    }

    /**
     * Screenshots, cover first.
     *
     * @return HasMany<ShareImage, $this>
     */
    public function images(): HasMany
    {
        return $this->hasMany(ShareImage::class)->orderBy('position');
    }

    /**
     * The listing's page on this site, which the listing names as its homepage.
     */
    public function url(): string
    {
        return route('shares.show', ['types' => self::FOLDERS[$this->type], 'id' => $this->listing_id]);
    }

    /**
     * Tells the owner, in the bell, what happened to their listing.
     *
     * @param  'share_merged'|'share_closed'|'share_mirrored'|'share_taken_down'  $kind
     */
    public function notifyOwner(string $kind, ?string $version = null, ?string $url = null): void
    {
        $this->user?->notify(new SiteActivity($kind, $this->name, $url ?? $this->url(), $version));
    }

    /**
     * Counts a download of the release by the player at this IP address, once a day per listing.
     * Only a hash of the address is kept, in the cache, until the day ends. The listing's total
     * and the release's own count go up together; updated_at stays as it is on both. Open pages
     * get the new total at once; the release list publishes it every six hours.
     */
    public function countDownload(string $ip, ShareRelease $release): void
    {
        $key = 'share-download:'.$this->id.':'.hash_hmac('sha256', self::downloader($ip), (string) config('app.key'));

        if (Cache::add($key, true, now()->endOfDay())) {
            DB::transaction(function () use ($release) {
                $this->newQuery()->whereKey($this->id)->toBase()->increment('downloads');
                $release->newQuery()->whereKey($release->id)->toBase()->increment('downloads');
            });

            ShareDownloadsChanged::dispatch($this->type, $this->listing_id, (int) $this->newQuery()->whereKey($this->id)->value('downloads'));
        }
    }

    /**
     * Who is downloading, for counting and rate limits: an IPv4 address, or an IPv6 address's /64,
     * since one connection usually gets a whole /64 and could otherwise count again from each address.
     */
    public static function downloader(string $ip): string
    {
        $bytes = @inet_pton($ip);

        return $bytes !== false && strlen($bytes) === 16 ? bin2hex(substr($bytes, 0, 8)).'::/64' : $ip;
    }

    /**
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return ['authors' => 'array', 'tags' => 'array', 'dependencies' => 'array', 'icon' => 'array'];
    }
}
