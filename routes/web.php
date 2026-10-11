<?php

use App\Http\Controllers\AccountController;
use App\Http\Controllers\DiscordAuthController;
use App\Http\Controllers\GitHubAuthController;
use App\Http\Controllers\ListingController;
use App\Http\Controllers\ModController;
use Illuminate\Support\Facades\Route;

Route::get('/', [ModController::class, 'index'])->name('home');
Route::get('/mods/{id}', [ModController::class, 'show'])->name('mods.show');

Route::middleware('guest')->group(function () {
    Route::inertia('/login', 'Auth/Login')->name('login');
    Route::get('/auth/github', [GitHubAuthController::class, 'redirect'])->name('github.login');
    Route::get('/auth/discord', [DiscordAuthController::class, 'redirect'])->name('discord.login');
});

// Both signing in and linking come back here, so these take guests and signed-in users alike.
Route::get('/auth/github/callback', [GitHubAuthController::class, 'callback']);
Route::get('/auth/discord/callback', [DiscordAuthController::class, 'callback']);

Route::middleware('auth')->group(function () {
    Route::get('/my-mods', [ModController::class, 'mine'])->name('mods.mine');
    Route::get('/settings/accounts', [AccountController::class, 'show'])->name('accounts');
    Route::delete('/settings/accounts/{provider}', [AccountController::class, 'unlink'])->name('accounts.unlink');
    Route::get('/auth/github/link', [GitHubAuthController::class, 'link'])->name('github.link');
    Route::get('/auth/discord/link', [DiscordAuthController::class, 'link'])->name('discord.link');
    Route::get('/auth/github/pull-requests', [GitHubAuthController::class, 'authorizePullRequests'])->name('github.pull-requests');
    Route::get('/listings/create', [ListingController::class, 'create'])->name('listings.create');
    Route::post('/listings', [ListingController::class, 'store'])->name('listings.store');
    Route::get('/mods/{id}/edit', [ListingController::class, 'edit'])->name('listings.edit');
    Route::put('/mods/{id}', [ListingController::class, 'update'])->name('listings.update');
    Route::post('/logout', [GitHubAuthController::class, 'logout'])->name('logout');
});
