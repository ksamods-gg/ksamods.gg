<?php

namespace App\Support\Share;

use RuntimeException;

/**
 * A refusal of the upload API, with one of the error codes of rule 16, which the API answers as
 * JSON `{ "error", "message" }` and the website shows as a message.
 */
class ShareError extends RuntimeException
{
    /**
     * @param  array<string, mixed>  $extra  More members of the answer, such as `checks` or `retry_after`.
     */
    public function __construct(public string $error, string $message, public int $status = 422, public array $extra = [])
    {
        parent::__construct($message);
    }

    /**
     * @return array<string, mixed>
     */
    public function toArray(): array
    {
        return ['error' => $this->error, 'message' => $this->getMessage(), ...$this->extra];
    }
}
