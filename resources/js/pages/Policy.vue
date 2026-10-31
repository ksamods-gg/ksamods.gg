<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import LegalPage from '@/components/LegalPage.vue';
import { privacy, terms } from '@/routes';

/**
 * The host policy that hosts.toml links (RFC 0094, spec/share-host.md rule 2): takedowns, abuse
 * reports with a contact and a response time, and the privacy of accounts.
 */
defineProps<{
    abuseEmail: string;
    discord: string | null;
    responseHours: number;
}>();
</script>

<template>
    <LegalPage
        :title="$t('Host policy')"
        updated="31 October 2026"
        :description="
            $t(
                'How ksamods.gg handles takedowns, abuse reports and privacy for the vehicles and saves it hosts.',
            )
        "
    >
        <p>
            {{
                $t(
                    'ksamods.gg hosts vehicles and save games for the KSA content index, as a share host under RFC 0094 of KSAModding. This page says how we handle takedowns, abuse reports and your privacy. It applies to every vehicle and save hosted here; the',
                )
            }}
            <Link :href="terms()">{{ $t('terms of use') }}</Link>
            {{ $t('apply too.') }}
        </p>

        <h2>{{ $t('Reporting abuse') }}</h2>
        <p>
            {{
                $t(
                    'Report spam, offensive names or descriptions, harmful files in a save, or anything that should not be here:',
                )
            }}
        </p>
        <ul>
            <li>
                {{ $t('By email to') }}
                <a :href="`mailto:${abuseEmail}`">{{ abuseEmail }}</a
                >.
            </li>
            <li v-if="discord">
                {{ $t('On') }}
                <a :href="discord" rel="noopener noreferrer">Discord</a>,
                {{ $t('to a ksamods.gg staff member.') }}
            </li>
        </ul>
        <p>
            {{
                $t(
                    'Name the vehicle or save (its page on ksamods.gg, or its id) and what is wrong. We answer every report within',
                )
            }}
            <strong>{{ $t(':hours hours', { hours: responseHours }) }}</strong
            >.
        </p>

        <h2>{{ $t('Takedowns') }}</h2>
        <p>
            {{
                $t(
                    "When a release has to go, we delete its archive and its copy on the Internet Archive, and remove it from its release list. Its download address then answers that it is gone, and the index marks the release as gone after its own waiting time. Content that must go faster is delisted by the index's stewards. A version number that was taken down is never given out again.",
                )
            }}
        </p>
        <p>
            {{
                $t(
                    'Copyright notices (DMCA) go to the same address. We act on valid notices within one business day.',
                )
            }}
        </p>
        <p>
            {{
                $t(
                    'We may also remove a whole listing for repeated abuse. If you think we got it wrong, reply to our message or write to the address above, and we look at it again.',
                )
            }}
        </p>

        <h2>{{ $t('Copies on the Internet Archive') }}</h2>
        <p>
            {{ $t('Every release is also uploaded to the') }}
            <a href="https://archive.org">Internet Archive</a>
            {{
                $t(
                    '(archive.org) as a public item of its own, under the license its author chose, so players can still install it if ksamods.gg goes away. The index lists that copy as a mirror. A takedown removes it there too.',
                )
            }}
        </p>

        <h2>{{ $t('Your privacy') }}</h2>
        <p>
            {{
                $t(
                    'Each listing names its owner in the index only by an opaque account id, such as',
                )
            }}
            <code>u_7Hk2pQ</code>,
            {{
                $t(
                    "which says nothing about you, and by the GitHub account id when you signed in with GitHub. The name you publish under is the listing's authors, which you choose.",
                )
            }}
        </p>
        <p>
            {{
                $t(
                    "What you publish goes into the public KSA content index, which keeps its history. When you delete your ksamods.gg account, the opaque id no longer points to anyone, and we remove your GitHub account id from your listings' owner records. The rest is in our",
                )
            }}
            <Link :href="privacy()">{{ $t('privacy policy') }}</Link
            >.
        </p>
    </LegalPage>
</template>
