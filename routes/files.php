<?php

use App\Http\Controllers\ShareController;
use App\Http\Middleware\KeepPrivatePagesOutOfSearch;
use Illuminate\Support\Facades\Route;

/*
 * A release's url in the release list (rule 21): it never changes, and answers 410 once taken down.
 * Outside the web group on purpose: no session, no cookies, so a CDN can cache the answer, and a
 * client fetching many archives leaves no session behind.
 */
Route::get('/share/files/{type}/{id}/{version}', [ShareController::class, 'download'])
    ->whereIn('type', ['vehicle', 'save'])
    ->where('version', '[0-9A-Za-z.+-]+')
    ->middleware(KeepPrivatePagesOutOfSearch::class)
    ->name('shares.download');
