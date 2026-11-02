<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import LegalPage from '@/components/LegalPage.vue';
import { terms } from '@/routes';
import type { LegalOperator } from '@/types';

defineProps<{ operator: LegalOperator }>();
</script>

<template>
    <LegalPage
        :title="$t('Privacy policy')"
        updated="2 November 2026"
        :description="
            $t(
                'What ksamods.gg stores about you, why, and how to have it deleted.',
            )
        "
    >
        <p>
            {{
                $t(
                    'This policy explains what personal data ksamods.gg processes, why, and what rights you have. ksamods.gg has no advertising, uses visitor statistics only if you allow them, and never sells your data.',
                )
            }}
        </p>

        <h2>{{ $t('Who is responsible') }}</h2>
        <p>
            {{
                $t(
                    'ksamods.gg is run by :operator, who is the controller under the EU General Data Protection Regulation (GDPR).',
                    { operator: operator.operator ?? $t('its operator') },
                )
            }}
            <template v-if="operator.email">
                {{ $t('Contact:') }}
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a
                >.
            </template>
        </p>

        <h2>{{ $t('When you browse') }}</h2>
        <p>
            {{
                $t(
                    "To deliver pages, the server receives your IP address and your browser's user agent. They are stored with your session, which expires after two hours of inactivity and is then deleted. The hosting infrastructure may also keep short-lived technical logs to keep the site running and secure.",
                )
            }}
        </p>

        <h2>{{ $t('Download counts') }}</h2>
        <p>
            {{
                $t(
                    'Vehicles and saves show how often they were downloaded. A download counts when you use a download button on the site, or when a client such as Borea reports that it installed one; fetching a file by itself, such as for a preview, does not count. Each player counts once a day per listing, and since no sign-in is needed, players are told apart by IP address (for IPv6, its /64 network).',
                )
            }}
        </p>
        <p>
            {{
                $t(
                    'For this, ksamods.gg keeps only a hash of the address and the listing, made with a secret key, never the address itself, and does not link it to your account. The hash is deleted at the end of the day (midnight UTC). To prevent abuse, these reports are also limited per address, with a counter that lasts about a minute.',
                )
            }}
        </p>

        <h2>{{ $t('Cookies') }}</h2>
        <p>
            {{ $t('These are strictly necessary, so they need no consent:') }}
        </p>
        <ul>
            <li>
                {{
                    $t(
                        'a session cookie that keeps you signed in, and a security cookie that protects forms against cross-site request forgery;',
                    )
                }}
            </li>
            <li>
                {{
                    $t(
                        'a remember cookie that keeps you signed in for weeks, only if you tick "Keep me signed in" when you log in;',
                    )
                }}
            </li>
            <li>
                {{
                    $t(
                        'a language cookie, only once you pick a language, and a consent cookie that stores your cookie choice for six months;',
                    )
                }}
            </li>
            <li>
                {{
                    $t(
                        "the game version you filter by, kept in your browser's local storage and never sent to the server.",
                    )
                }}
            </li>
        </ul>
        <p>
            {{
                $t(
                    'Statistics cookies are set only if you allow them: Google Analytics (cookies named _ga and _ga_…, kept for up to two years) counts visits and pages viewed, so we know what to improve. Google receives your IP address, browser details and the pages you view. Without your consent, Google Analytics is not loaded at all. You can change your choice any time under Cookie settings at the bottom of every page; withdrawing removes its cookies.',
                )
            }}
        </p>

        <h2>{{ $t('When you sign in') }}</h2>
        <p>
            {{
                $t(
                    'You sign in with GitHub or Discord. ksamods.gg has no passwords.',
                )
            }}
        </p>
        <ul>
            <li>
                <strong>{{ $t('GitHub:') }}</strong>
                {{
                    $t(
                        "your GitHub account ID, username, email address (if GitHub shares one) and avatar. ksamods.gg also stores an access token, encrypted, that lets it open pull requests on public repositories in your name. It is used only when you submit or edit a listing, and to check whether you own a mod's repository.",
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Discord:') }}</strong>
                {{
                    $t(
                        'your Discord account ID, username, display name and avatar. ksamods.gg does not request your Discord email address.',
                    )
                }}
            </li>
        </ul>
        <p>
            {{
                $t(
                    'Your display name and avatar appear next to everything you post. You can unlink either account on your account page, as long as the other stays linked; its data is then removed straight away.',
                )
            }}
        </p>

        <h2>{{ $t('What you post') }}</h2>
        <ul>
            <li>
                <strong>{{ $t('Reviews and comments') }}</strong
                >{{
                    $t(
                        ', including replies, are public, shown with your name and avatar.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Reports') }}</strong>
                {{
                    $t(
                        '(the reason and any details you give) are seen only by moderators.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Notifications') }}</strong>
                {{
                    $t(
                        "about replies, mentions, new releases of what you follow and your own shared listings are stored for you and delivered live by ksamods.gg's own server. What you follow is visible only to you.",
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Public profile') }}</strong
                >{{
                    $t(
                        ', only if you turn it on: your username, bio and links, your GitHub and Discord names if you choose to show them, and the mods, shared vehicles and saves, reviews and comments you leave visible. Turn it off any time in your settings.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Email digest:') }}</strong>
                {{
                    $t(
                        'if you turn it on, the email address you give for it, whether you confirmed it, how often you want it and when the last one went out. It is used only to send the digest and its confirmation email. Turn it off with the link in any digest or in your notification settings.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Mod listings') }}</strong>
                {{
                    $t(
                        "you submit or edit become pull requests on the KSAModding content index, opened from your GitHub account. They are public on GitHub, and GitHub's privacy statement covers them; ksamods.gg cannot delete them.",
                    )
                }}
            </li>
        </ul>

        <h2>{{ $t('Legal bases') }}</h2>
        <p>
            {{
                $t(
                    'Providing your account and the features you use is necessary to perform our agreement with you (Art. 6(1)(b) GDPR). Sessions, security logs and moderation rest on the legitimate interest in running a safe, working site, and download counts on the legitimate interest in showing honest download numbers (Art. 6(1)(f) GDPR). Statistics rest on your consent (Art. 6(1)(a) GDPR), which you can withdraw at any time.',
                )
            }}
        </p>

        <h2>{{ $t('How long data is kept') }}</h2>
        <p>
            {{
                $t(
                    'Your account data, posts and reports are kept until you delete them or delete your account. When you delete a comment that has replies, it is hidden but kept so the thread still reads, until your account is deleted. Deleting your account removes your reviews, comments, reports, follows, digest email address and notifications, the replies other people wrote under your comments, and the notifications others got about your comments. The hashes used to count downloads are deleted at the end of each day.',
                )
            }}
        </p>

        <h2>{{ $t('Who else receives data') }}</h2>
        <ul>
            <li>
                <strong>{{ $t('The hosting provider') }}</strong>
                {{
                    $t(
                        "that runs the servers, processing data on ksamods.gg's behalf.",
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('GitHub and Discord') }}</strong
                >{{
                    $t(
                        ', when you sign in with them and when ksamods.gg acts on GitHub for you.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('The email provider') }}</strong>
                {{
                    $t(
                        "that delivers the digest and its confirmation email, which receives your digest address and the email's content.",
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Image and download hosts.') }}</strong>
                {{
                    $t(
                        'Avatars, mod icons, images in mod descriptions and downloads load straight from GitHub, Discord and wherever mod authors host them, so those servers see your IP address.',
                    )
                }}
            </li>
            <li>
                <strong>{{ $t('Google') }}</strong>
                {{ $t('(Google Analytics), only if you allow statistics.') }}
            </li>
        </ul>
        <p>
            {{
                $t(
                    'GitHub, Discord and Google are based in the United States. Transfers to them rely on the EU–US Data Privacy Framework or standard contractual clauses.',
                )
            }}
        </p>

        <h2>{{ $t('Your rights') }}</h2>
        <p>
            {{
                $t(
                    'You have the right to access your data, to have it corrected or deleted, to restrict or object to its processing, and to receive it in a portable format. You can delete your account yourself on your account page. To use any of the other rights,',
                )
            }}
            <template v-if="operator.email">
                {{ $t('email') }}
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a>
            </template>
            <template v-else>{{ $t('contact the operator') }}</template>
            {{
                $t(
                    'from an address you can show is yours, or say which GitHub or Discord account is yours so it can be confirmed. You may also complain to the data protection authority where you live.',
                )
            }}
        </p>

        <h2>{{ $t('Children') }}</h2>
        <p>
            {{
                $t(
                    "You must be at least 13 to make an account. Where your country sets a higher age for consenting to online services (up to 16 in the EU), you need a parent's permission until you reach it. See the",
                )
            }}
            <Link :href="terms()">{{ $t('terms of use') }}</Link
            >.
        </p>

        <h2>{{ $t('Changes') }}</h2>
        <p>
            {{
                $t(
                    'This policy changes when the site does. The date at the top shows the latest version.',
                )
            }}
        </p>
    </LegalPage>
</template>
