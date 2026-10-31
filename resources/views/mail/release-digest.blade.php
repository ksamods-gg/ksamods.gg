<x-mail::message>
# New releases

Hi {{ $name }}, here is what came out of the mods, vehicles and saves you follow:

@foreach ($releases as $release)
- [{{ $release['name'] }}]({{ $release['url'] }}) {{ $release['version'] }}
@endforeach

<x-mail::button :url="config('app.url')">
Open ksamods.gg
</x-mail::button>

<x-slot:subcopy>
You get this {{ $frequency }}. [Change how often]({{ $settings }}) or [unsubscribe]({{ $unsubscribe }}).
</x-slot:subcopy>
</x-mail::message>
