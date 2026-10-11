<?php

use App\Http\Controllers\GitHubAuthController;
use App\Http\Controllers\ListingController;
use App\Http\Controllers\ModController;
use Illuminate\Support\Facades\Route;

Route::get('/', [ModController::class, 'index'])->name('home');
Route::get('/mods/{id}', [ModController::class, 'show'])->name('mods.show');

Route::get('/auth/github', [GitHubAuthController::class, 'redirect'])->middleware('guest')->name('login');
Route::get('/auth/github/callback', [GitHubAuthController::class, 'callback']);

Route::middleware('auth')->group(function () {
    Route::get('/my-mods', [ModController::class, 'mine'])->name('mods.mine');
    Route::get('/auth/github/pull-requests', [GitHubAuthController::class, 'authorizePullRequests'])->name('github.pull-requests');
    Route::get('/listings/create', [ListingController::class, 'create'])->name('listings.create');
    Route::post('/listings', [ListingController::class, 'store'])->name('listings.store');
    Route::get('/mods/{id}/edit', [ListingController::class, 'edit'])->name('listings.edit');
    Route::put('/mods/{id}', [ListingController::class, 'update'])->name('listings.update');
    Route::post('/logout', [GitHubAuthController::class, 'logout'])->name('logout');
});
