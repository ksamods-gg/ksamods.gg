<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import LegalPage from '@/components/LegalPage.vue';
import { privacy } from '@/routes';
import type { LegalOperator } from '@/types';

defineProps<{ operator: LegalOperator }>();
</script>

<template>
    <LegalPage title="Terms of use" updated="14 October 2026">
        <p>
            These terms apply when you use ksamods.gg, a free, community-run
            site for finding and discussing mods for Kitten Space Agency. By
            using it you agree to them. The
            <Link :href="privacy()">privacy policy</Link> explains how your data
            is handled.
        </p>

        <h2>Who runs ksamods.gg</h2>
        <p>
            ksamods.gg is run by {{ operator.operator ?? 'its operator' }}.
            <template v-if="operator.email">
                Contact:
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a
                >.
            </template>
            It is an unofficial fan project, not affiliated with or endorsed by
            the makers of Kitten Space Agency. Kitten Space Agency and related
            names belong to their owners.
        </p>

        <h2>Accounts</h2>
        <p>
            You sign in with GitHub or Discord, and their terms apply to those
            accounts too. You must be at least 13, and where your country sets a
            higher age for consenting to online services, you need a parent's
            permission until you reach it. You are responsible for what happens
            under your account.
        </p>

        <h2>What you post</h2>
        <p>
            Your reviews, comments and other posts stay yours. By posting, you
            let ksamods.gg store and show them on the site for as long as they
            are up. Do not post anything that:
        </p>
        <ul>
            <li>is illegal, or infringes someone else's rights;</li>
            <li>harasses, threatens or demeans people;</li>
            <li>is spam, advertising, or links to malware;</li>
            <li>
                impersonates someone, or shares other people's personal data;
            </li>
            <li>is a review of something other than the mod it is on.</li>
        </ul>

        <h2>Moderation</h2>
        <p>
            Use the report button to flag a post that breaks these terms or the
            law. Moderators may remove posts and suspend or close accounts that
            break them. If you think a decision was wrong,
            <template v-if="operator.email">
                email
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a>
            </template>
            <template v-else>contact the operator</template>
            and it will be looked at again.
        </p>

        <h2>Mods are made by others</h2>
        <p>
            Mods listed here come from the KSAModding content index and are made
            and hosted by their authors, not by ksamods.gg. ksamods.gg does not
            check them, and cannot promise that any mod is safe, works or suits
            your game. You install mods at your own risk, so keep backups of
            your saves.
        </p>

        <h2>Submitting listings</h2>
        <p>
            Submitting or editing a listing opens a pull request on the content
            index from your GitHub account. Its maintainers decide whether it is
            accepted, under the index's own rules. Only submit mods you have the
            right to list.
        </p>

        <h2>The service</h2>
        <p>
            ksamods.gg is provided free and as it is. It may change, be
            unavailable at times, or end. You may stop using it at any time, and
            you can delete your account on your account page.
        </p>

        <h2>Liability</h2>
        <p>
            ksamods.gg is liable without limit for damage caused intentionally
            or through gross negligence, for injury to life, body or health, and
            wherever the law does not allow liability to be limited. Otherwise,
            for a free service like this one, it is not liable for damage
            arising from mods, from content other people post, or from the site
            being unavailable.
        </p>

        <h2>Changes and law</h2>
        <p>
            These terms may change; the date at the top shows the latest
            version.
            <template v-if="operator.country">
                The law of {{ operator.country }} applies.
            </template>
            If you use ksamods.gg as a consumer, you keep the protection of the
            mandatory laws of the country where you live.
        </p>
    </LegalPage>
</template>
