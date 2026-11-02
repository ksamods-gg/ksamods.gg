<?php

use App\Http\Controllers\Api\ShareDeviceController;
use App\Http\Controllers\Api\ShareListingController;
use App\Http\Controllers\Api\ShareUploadController;
use Illuminate\Support\Facades\Route;

/*
 * The share host's upload API (RFC 0094, spec/share-host.md rule 15), served at
 * https://ksamods.gg/api/share/v1/, which hosts.toml names as `api`.
 */
Route::middleware('throttle:share-sign-in')->group(function () {
    Route::post('/oauth/device_authorization', [ShareDeviceController::class, 'authorization'])->name('share.api.device');
    Route::post('/oauth/token', [ShareDeviceController::class, 'token'])->name('share.api.token');
});

// Counts a download, from the site's buttons and from clients; no sign-in.
Route::post('/listings/{type}/{id}/downloads', [ShareListingController::class, 'download'])
    ->whereIn('type', ['vehicle', 'save'])
    ->middleware('throttle:share-downloads')
    ->name('share.api.downloads');

Route::middleware(['auth:api', 'throttle:share-uploads'])->group(function () {
    Route::post('/uploads', [ShareUploadController::class, 'store'])->name('share.api.uploads');
    Route::post('/uploads/{upload}/publish', [ShareUploadController::class, 'publish'])->name('share.api.publish');

    // The owner's own listing: its text, taking a version down, and its screenshots (RFC 0058 images).
    Route::patch('/listings/{type}/{id}', [ShareListingController::class, 'update'])->whereIn('type', ['vehicle', 'save'])->name('share.api.listings.update');
    Route::post('/listings/{type}/{id}/releases/{version}/take-down', [ShareListingController::class, 'takeDown'])->whereIn('type', ['vehicle', 'save'])->where('version', '[0-9A-Za-z.+-]+')->name('share.api.releases.take-down');
    Route::get('/listings/{type}/{id}/images', [ShareListingController::class, 'index'])->whereIn('type', ['vehicle', 'save'])->name('share.api.images');
    Route::post('/listings/{type}/{id}/images', [ShareListingController::class, 'store'])->whereIn('type', ['vehicle', 'save'])->name('share.api.images.store');
    Route::delete('/listings/{type}/{id}/images/{image}', [ShareListingController::class, 'destroy'])->whereIn('type', ['vehicle', 'save'])->whereNumber('image')->name('share.api.images.destroy');
});
