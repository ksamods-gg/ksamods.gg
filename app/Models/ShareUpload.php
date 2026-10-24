<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Concerns\HasUlids;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Prunable;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\Storage;

/**
 * An uploaded archive with the result of its checks, waiting to be published or to expire.
 *
 * @property string $id
 * @property int $user_id
 * @property int|null $share_id
 * @property 'vehicle'|'save'|null $type
 * @property string $path
 * @property int $size
 * @property string $sha256
 * @property string|null $folder
 * @property string|null $game_version
 * @property list<array{id: string, kind: string, min?: string, max?: string}> $dependencies
 * @property list<array{name: string, passed: bool, message: string}> $checks
 * @property bool $passed
 * @property string|null $client
 * @property Carbon $expires_at
 * @property Carbon|null $published_at
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read User $user
 * @property-read Share|null $share
 */
class ShareUpload extends Model
{
    use HasUlids, Prunable;

    protected $guarded = [];

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * @return BelongsTo<Share, $this>
     */
    public function share(): BelongsTo
    {
        return $this->belongsTo(Share::class);
    }

    public function expired(): bool
    {
        return $this->expires_at->isPast();
    }

    /**
     * Expired uploads are discarded with their archives; published ones are done with theirs.
     */
    public function prunable(): mixed
    {
        return static::where('expires_at', '<', now())->orWhereNotNull('published_at');
    }

    protected function pruning(): void
    {
        Storage::disk(config('share.disk'))->delete($this->path);
    }

    /**
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return [
            'size' => 'integer',
            'dependencies' => 'array',
            'checks' => 'array',
            'passed' => 'boolean',
            'expires_at' => 'datetime',
            'published_at' => 'datetime',
        ];
    }
}
