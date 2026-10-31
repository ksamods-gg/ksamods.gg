<x-mail::message>
# Sending works

This is a test email from ksamods.gg. If you can read it, the mail settings are right.

<x-mail::button :url="config('app.url')">
Open ksamods.gg
</x-mail::button>

<x-slot:subcopy>
Sent by `php artisan mail:test`.
</x-slot:subcopy>
</x-mail::message>
