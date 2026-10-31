<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<html xmlns="http://www.w3.org/1999/xhtml" lang="{{ str_replace('_', '-', app()->getLocale()) }}">
<head>
<title>{{ config('app.name') }}</title>
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
<meta name="color-scheme" content="light dark">
<meta name="supported-color-schemes" content="light dark">
{{-- The site's typeface, where the mail app loads web fonts; the rest fall back to the system font. --}}
<link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;500;600&display=swap" rel="stylesheet">
<style>
@media only screen and (max-width: 600px) {
.inner-body {
width: 100% !important;
}

.footer {
width: 100% !important;
}

.content-cell {
padding: 24px !important;
}
}

@media only screen and (max-width: 500px) {
.button {
width: 100% !important;
}
}

/* The site's dark palette, for mail apps that follow the system's dark mode. */
@media (prefers-color-scheme: dark) {
body, .wrapper, .body {
background-color: #0a0a0a !important;
}

.inner-body {
background-color: #161615 !important;
border-color: #3E3E3A !important;
}

h1, h2, h3, .header a, .panel-content, .panel-content p, .table th {
color: #EDEDEC !important;
}

p, ul, ol, li, .table td {
color: #c4c3be !important;
}

a {
color: #2aa9a0 !important;
}

.footer p, .footer a, .subcopy p {
color: #A1A09A !important;
}

.subcopy {
border-top-color: #3E3E3A !important;
}

.button-primary, .button-blue {
background-color: #EDEDEC !important;
border-color: #EDEDEC !important;
color: #0a0a0a !important;
}

.panel-content {
background-color: #0a2321 !important;
}
}
</style>
{!! $head ?? '' !!}
</head>
<body>

<table class="wrapper" width="100%" cellpadding="0" cellspacing="0" role="presentation">
<tr>
<td align="center">
<table class="content" width="100%" cellpadding="0" cellspacing="0" role="presentation">
{!! $header ?? '' !!}

<!-- Email Body -->
<tr>
<td class="body" width="100%" cellpadding="0" cellspacing="0" style="border: hidden !important;">
<table class="inner-body" align="center" width="570" cellpadding="0" cellspacing="0" role="presentation">
<!-- Body content -->
<tr>
<td class="content-cell">
{!! Illuminate\Mail\Markdown::parse($slot) !!}

{!! $subcopy ?? '' !!}
</td>
</tr>
</table>
</td>
</tr>

{!! $footer ?? '' !!}
</table>
</td>
</tr>
</table>
</body>
</html>
