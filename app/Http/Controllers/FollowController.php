<?php

namespace App\Http\Controllers;

use App\Models\Follow;
use App\Models\SeenRelease;
use App\Support\ContentIndex;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class FollowController extends Controller
{
    /**
     * Follows a mod, vehicle or save, or stops following it.
     */
    public function toggle(Request $request): RedirectResponse
    {
        $validated = $request->validate([
            'kind' => ['required', Rule::in(Follow::KINDS)],
            'id' => ['required', 'string', 'max:64'],
        ]);
        $follow = $request->user()->follows()->where('kind', $validated['kind'])->where('listing_id', $validated['id'])->first();

        if ($follow !== null) {
            $follow->delete();

            return back();
        }

        $request->user()->follows()->create(['kind' => $validated['kind'], 'listing_id' => $validated['id']]);

        // A mod's newest version so far, so only a later one counts as new.
        if ($validated['kind'] === 'mod') {
            $version = SeenRelease::newestVersion(collect(ContentIndex::snapshot()['listings'])->firstWhere('id', $validated['id']) ?? []);

            if ($version !== null) {
                SeenRelease::firstOrCreate(['listing_id' => $validated['id']], ['version' => $version]);
            }
        }

        return back();
    }
}
