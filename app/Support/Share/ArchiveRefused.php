<?php

namespace App\Support\Share;

use RuntimeException;

/**
 * Stops the archive checks at the first failure, which ShareArchive has already recorded.
 */
class ArchiveRefused extends RuntimeException {}
