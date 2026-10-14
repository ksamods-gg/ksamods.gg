<?php

namespace App\Http\Controllers;

use Inertia\Inertia;
use Inertia\Response;

class LegalController extends Controller
{
    public function privacy(): Response
    {
        return Inertia::render('Legal/Privacy', ['operator' => config('legal')]);
    }

    public function terms(): Response
    {
        return Inertia::render('Legal/Terms', ['operator' => config('legal')]);
    }
}
