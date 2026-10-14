<?php

return [

    /*
    |--------------------------------------------------------------------------
    | Operator
    |--------------------------------------------------------------------------
    |
    | Who runs the site, named in the privacy policy and terms of use. Set in the
    | environment rather than here so the operator's details stay out of the repository.
    |
    */

    'operator' => env('LEGAL_OPERATOR_NAME'),

    'email' => env('LEGAL_CONTACT_EMAIL'),

    // The country whose law governs the terms, such as "Germany".
    'country' => env('LEGAL_COUNTRY'),

];
