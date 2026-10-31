<?php

namespace App\Models;

use App\Support\Share\DataFiles;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

/**
 * One screenshot of a vehicle or save: a WebP on the share disk.
 *
 * @property int $id
 * @property int $share_id
 * @property int $position
 * @property string $path
 * @property string $sha256
 * @property int $width
 * @property int $height
 * @property int $size
 * @property-read Share $share
 */
class ShareImage extends Model
{
    protected $guarded = [];

    /**
     * @return BelongsTo<Share, $this>
     */
    public function share(): BelongsTo
    {
        return $this->belongsTo(Share::class);
    }

    public function url(): string
    {
        return DataFiles::url($this->path);
    }

    /**
     * Its RFC 0058 image record, as the listing document's description images name it.
     *
     * @return array{id: string, url: string, sha256: string, width: int, height: int, size: int}
     */
    public function record(): array
    {
        return [
            'id' => "screenshot-{$this->id}",
            'url' => $this->url(),
            'sha256' => $this->sha256,
            'width' => $this->width,
            'height' => $this->height,
            'size' => $this->size,
        ];
    }
}
