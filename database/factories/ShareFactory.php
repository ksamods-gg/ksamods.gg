<?php

namespace Database\Factories;

use App\Models\Share;
use App\Models\User;
use Illuminate\Database\Eloquent\Factories\Factory;

/**
 * @extends Factory<Share>
 */
class ShareFactory extends Factory
{
    /**
     * Define the model's default state.
     *
     * @return array<string, mixed>
     */
    public function definition(): array
    {
        return [
            'type' => 'vehicle',
            'listing_id' => 'ExampleRover',
            'user_id' => User::factory()->state(['share_account' => 'u_'.fake()->unique()->bothify('????????')]),
            'account' => fn (array $attributes) => User::whereKey($attributes['user_id'])->value('share_account'),
            'name' => 'Example Rover',
            'authors' => ['Maxi'],
            'abstract' => 'A six-wheel rover for the Moon.',
            'license' => 'CC-BY-4.0',
            'tags' => ['rover'],
            'dependencies' => [],
        ];
    }
}
