<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

/**
 * @property int $translation_id
 * @property int $user_id
 * @property int $value
 */
class TranslationVote extends Model
{
    public $timestamps = false;

    public $incrementing = false;

    protected $primaryKey = 'translation_id';

    protected $guarded = [];
}
