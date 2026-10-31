<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

/**
 * A language of the site's community translations: requested by a player, then made active by
 * an admin.
 *
 * @property int $id
 * @property string $code
 * @property string $name
 * @property string $native_name
 * @property bool $active
 * @property int|null $requested_by
 */
class Language extends Model
{
    protected $guarded = [];

    /**
     * @return HasMany<Translation, $this>
     */
    public function translations(): HasMany
    {
        return $this->hasMany(Translation::class);
    }

    protected function casts(): array
    {
        return ['active' => 'boolean'];
    }
}
