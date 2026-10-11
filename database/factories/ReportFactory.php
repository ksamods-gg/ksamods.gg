<?php

namespace Database\Factories;

use App\Models\Comment;
use App\Models\Report;
use App\Models\User;
use Illuminate\Database\Eloquent\Factories\Factory;

/**
 * @extends Factory<Report>
 */
class ReportFactory extends Factory
{
    /**
     * Define the model's default state.
     *
     * @return array<string, mixed>
     */
    public function definition(): array
    {
        return [
            'user_id' => User::factory(),
            'reportable_type' => (new Comment)->getMorphClass(),
            'reportable_id' => Comment::factory(),
            'reason' => 'spam',
            'details' => null,
        ];
    }
}
