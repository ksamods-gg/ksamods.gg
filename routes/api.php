<?php

use App\Http\Controllers\Api\ShareDeviceController;
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

Route::middleware(['auth:api', 'throttle:share-uploads'])->group(function () {
    Route::post('/uploads', [ShareUploadController::class, 'store'])->name('share.api.uploads');
    Route::post('/uploads/{upload}/publish', [ShareUploadController::class, 'publish'])->name('share.api.publish');
});
