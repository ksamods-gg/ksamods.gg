<?php

use Inertia\Testing\AssertableInertia as Assert;

beforeEach(function () {
    $this->withoutVite();

    config(['legal' => ['operator' => 'Jane Doe', 'email' => 'hello@example.com', 'country' => 'Germany']]);
});

test('guests can read the privacy policy', function () {
    $this->get('/privacy')
        ->assertOk()
        ->assertInertia(fn (Assert $page) => $page
            ->component('Legal/Privacy')
            ->where('operator.operator', 'Jane Doe')
            ->where('operator.email', 'hello@example.com'));
});

test('guests can read the terms of use', function () {
    $this->get('/terms')
        ->assertOk()
        ->assertInertia(fn (Assert $page) => $page
            ->component('Legal/Terms')
            ->where('operator.country', 'Germany'));
});
