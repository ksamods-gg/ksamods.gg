<?php

namespace App\Models\Concerns;

use App\Models\Report;
use Illuminate\Database\Eloquent\Relations\MorphMany;

/**
 * A post users can report. Deleting it resolves its open reports, since there is nothing left
 * for a moderator to act on.
 */
trait Reportable
{
    public static function bootReportable(): void
    {
        static::deleted(fn (self $post) => $post->reports()->open()->update([
            'resolved_at' => now(),
            'resolved_by' => auth()->id(),
        ]));
    }

    /**
     * @return MorphMany<Report, $this>
     */
    public function reports(): MorphMany
    {
        return $this->morphMany(Report::class, 'reportable');
    }
}
