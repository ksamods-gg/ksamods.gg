<?php

namespace App\Models;

use App\Notifications\SiteActivity;
use Database\Factories\ShareFactory;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Support\Carbon;
use Illuminate\Support\Collection;

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
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read User|null $user
 * @property-read Collection<int, ShareRelease> $releases
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
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return ['authors' => 'array', 'tags' => 'array', 'dependencies' => 'array'];
    }
}
