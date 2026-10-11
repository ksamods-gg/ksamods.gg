<?php

namespace App\Enums;

/**
 * Staff roles. Moderators handle reviews, comments and reports; admins also manage roles.
 * Everyone else has no role.
 */
enum Role: string
{
    case Moderator = 'moderator';
    case Admin = 'admin';
}
