{!! '<?xml version="1.0" encoding="UTF-8"?>' !!}
<feed xmlns="http://www.w3.org/2005/Atom">
    <title>{{ $title }}</title>
    <link rel="self" type="application/atom+xml" href="{{ $self }}"/>
    <link rel="alternate" type="text/html" href="{{ $alternate }}"/>
    <id>{{ $self }}</id>
    <updated>{{ $updated->toAtomString() }}</updated>
    <generator uri="{{ url('/') }}">ksamods.gg</generator>
@foreach ($items as $item)
    <entry>
        <title>{{ $item['title'] }}</title>
        <link rel="alternate" type="text/html" href="{{ $item['url'] }}"/>
        <id>{{ $item['id'] }}</id>
        <updated>{{ $item['date']->toAtomString() }}</updated>
@foreach ($item['authors'] ?: ['Unknown'] as $author)
        <author><name>{{ $author }}</name></author>
@endforeach
        <summary>{{ $item['summary'] }}</summary>
    </entry>
@endforeach
</feed>
