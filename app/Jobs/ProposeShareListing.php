<?php

namespace App\Jobs;

use App\Models\Share;
use App\Support\Share\Attestation;
use App\Support\Share\DataFiles;
use App\Support\Share\ListingDocument;
use App\Support\Share\ShareBot;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Queue\Queueable;

/**
 * Attests a listing's document and proposes it to the index from the bot's fork, in that order
 * (rule 9). Every push gets its own attestation, because an attestation binds the exact content
 * (rule 13). Runs inline when publishing, and from the queue when GitHub could not be reached.
 */
class ProposeShareListing implements ShouldQueue
{
    use Queueable;

    public int $tries = 5;

    // A listing deleted meanwhile (share:purge) has nothing left to propose.
    public bool $deleteWhenMissingModels = true;

    /** @var list<int> */
    public array $backoff = [60, 300, 900, 3600];

    public function __construct(public Share $share, public bool $new) {}

    public function handle(): void
    {
        self::run($this->share, $this->new);
    }

    /**
     * @return string The pull request's URL.
     */
    public static function run(Share $share, bool $new): string
    {
        $share->refresh();
        $path = ListingDocument::path($share);
        $files = [$path => ListingDocument::toml($share)];
        $digest = Attestation::digest($files);
        $name = "{$share->type}/{$share->listing_id}";

        DataFiles::writeAttestation($digest, [$name => $share->account]);

        $title = $new ? "Add the {$share->type} {$share->listing_id}" : "Update the {$share->type} {$share->listing_id}";
        $body = implode("\n\n", [
            ($new ? 'A new listing' : 'An update of its owner to the listing').", published on ksamods.gg as a share host (RFC 0094): {$share->url()}",
            'Ownership is proven by attestation: the host attests this exact change, for the account that owns the listing, in '
                .DataFiles::url("attestations/{$digest}.json").' (signed, with its signature at the same path with .sig appended), '
                .'and the owner record is '.DataFiles::url("owners/{$name}.json").'.',
            'This pull request was opened by the ksamods.gg bot on behalf of a player. The host never merges, approves or labels pull requests.',
        ]);

        return ShareBot::propose($share, $files, $title, $body);
    }
}
