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
        title="Host policy"
        updated="23 October 2026"
        description="How ksamods.gg handles takedowns, abuse reports and privacy for the vehicles and saves it hosts."
    >
        <p>
            ksamods.gg hosts vehicles and save games for the KSA content index,
            as a share host under RFC 0094 of KSAModding. This page says how we
            handle takedowns, abuse reports and your privacy. It applies to
            every vehicle and save hosted here; the
            <Link :href="terms()">terms of use</Link> apply too.
        </p>

        <h2>Reporting abuse</h2>
        <p>
            Report spam, offensive names or descriptions, harmful files in a
            save, or anything that should not be here:
        </p>
        <ul>
            <li>
                By email to
                <a :href="`mailto:${abuseEmail}`">{{ abuseEmail }}</a
                >.
            </li>
            <li v-if="discord">
                On <a :href="discord" rel="noopener noreferrer">Discord</a>, to
                a ksamods.gg staff member.
            </li>
        </ul>
        <p>
            Name the vehicle or save (its page on ksamods.gg, or its id) and
            what is wrong. We answer every report within
            <strong>{{ responseHours }} hours</strong>.
        </p>

        <h2>Takedowns</h2>
        <p>
            When a release has to go, we delete its archive and remove it from
            its release list. Its download address then answers that it is gone,
            and the index marks the release as gone after its own waiting time.
            Content that must go faster is delisted by the index's stewards. A
            version number that was taken down is never given out again.
        </p>
        <p>
            Copyright notices (DMCA) go to the same address. We act on valid
            notices within one business day.
        </p>
        <p>
            We may also remove a whole listing for repeated abuse. If you think
            we got it wrong, reply to our message or write to the address above,
            and we look at it again.
        </p>

        <h2>Your privacy</h2>
        <p>
            Each listing names its owner in the index only by an opaque account
            id, such as <code>u_7Hk2pQ</code>, which says nothing about you, and
            by the GitHub account id when you signed in with GitHub. The name
            you publish under is the listing's authors, which you choose.
        </p>
        <p>
            What you publish goes into the public KSA content index, which keeps
            its history. When you delete your ksamods.gg account, the opaque id
            no longer points to anyone, and we remove your GitHub account id
            from your listings' owner records. The rest is in our
            <Link :href="privacy()">privacy policy</Link>.
        </p>
    </LegalPage>
</template>
