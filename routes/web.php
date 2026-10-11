<?php

use App\Http\Controllers\GitHubAuthController;
use Illuminate\Support\Facades\Route;

Route::inertia('/', 'Welcome')->name('home');

Route::middleware('guest')->group(function () {
    Route::get('/auth/github', [GitHubAuthController::class, 'redirect'])->name('login');
    Route::get('/auth/github/callback', [GitHubAuthController::class, 'callback']);
});

Route::post('/logout', [GitHubAuthController::class, 'logout'])->middleware('auth')->name('logout');
