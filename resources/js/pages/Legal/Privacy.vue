<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import LegalPage from '@/components/LegalPage.vue';
import { terms } from '@/routes';
import type { LegalOperator } from '@/types';

defineProps<{ operator: LegalOperator }>();
</script>

<template>
    <LegalPage title="Privacy policy" updated="14 October 2026">
        <p>
            This policy explains what personal data ksamods.gg processes, why,
            and what rights you have. ksamods.gg has no advertising, analytics
            or tracking, and never sells your data.
        </p>

        <h2>Who is responsible</h2>
        <p>
            ksamods.gg is run by
            {{ operator.operator ?? 'its operator' }}, who is the controller
            under the EU General Data Protection Regulation (GDPR).
            <template v-if="operator.email">
                Contact:
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a
                >.
            </template>
        </p>

        <h2>When you browse</h2>
        <p>
            To deliver pages, the server receives your IP address and your
            browser's user agent. They are stored with your session, which
            expires after two hours of inactivity and is then deleted. The
            hosting infrastructure may also keep short-lived technical logs to
            keep the site running and secure.
        </p>
        <p>
            The site sets two cookies, and both are strictly necessary: a
            session cookie that keeps you signed in, and a security cookie that
            protects forms against cross-site request forgery. It sets no other
            cookies, so it asks for no cookie consent.
        </p>

        <h2>When you sign in</h2>
        <p>You sign in with GitHub or Discord. ksamods.gg has no passwords.</p>
        <ul>
            <li>
                <strong>GitHub:</strong> your GitHub account ID, username, email
                address (if GitHub shares one) and avatar. ksamods.gg also
                stores an access token, encrypted, that lets it open pull
                requests on public repositories in your name. It is used only
                when you submit or edit a listing, and to check whether you own
                a mod's repository.
            </li>
            <li>
                <strong>Discord:</strong> your Discord account ID, username,
                display name and avatar. ksamods.gg does not request your
                Discord email address.
            </li>
        </ul>
        <p>
            Your display name and avatar appear next to everything you post. You
            can unlink either account on your account page, as long as the other
            stays linked; its data is then removed straight away.
        </p>

        <h2>What you post</h2>
        <ul>
            <li>
                <strong>Reviews and comments</strong>, including replies, are
                public, shown with your name and avatar.
            </li>
            <li>
                <strong>Reports</strong> (the reason and any details you give)
                are seen only by moderators.
            </li>
            <li>
                <strong>Notifications</strong> about replies and mentions are
                stored for you and delivered live by ksamods.gg's own server.
            </li>
            <li>
                <strong>Mod listings</strong> you submit or edit become pull
                requests on the KSAModding content index, opened from your
                GitHub account. They are public on GitHub, and GitHub's privacy
                statement covers them; ksamods.gg cannot delete them.
            </li>
        </ul>

        <h2>Legal bases</h2>
        <p>
            Providing your account and the features you use is necessary to
            perform our agreement with you (Art. 6(1)(b) GDPR). Sessions,
            security logs and moderation rest on the legitimate interest in
            running a safe, working site (Art. 6(1)(f) GDPR).
        </p>

        <h2>How long data is kept</h2>
        <p>
            Your account data, posts and reports are kept until you delete them
            or ask for your account to be deleted. When you delete a comment
            that has replies, it is hidden but kept so the thread still reads,
            until your account is deleted. Deleting your account removes your
            reviews, comments, reports and notifications.
        </p>

        <h2>Who else receives data</h2>
        <ul>
            <li>
                <strong>The hosting provider</strong> that runs the servers,
                processing data on ksamods.gg's behalf.
            </li>
            <li>
                <strong>GitHub and Discord</strong>, when you sign in with them
                and when ksamods.gg acts on GitHub for you.
            </li>
            <li>
                <strong>Image and download hosts.</strong> Avatars, mod icons,
                images in mod descriptions and downloads load straight from
                GitHub, Discord and wherever mod authors host them, so those
                servers see your IP address.
            </li>
        </ul>
        <p>
            GitHub and Discord are based in the United States. Transfers to them
            rely on the EU–US Data Privacy Framework or standard contractual
            clauses.
        </p>

        <h2>Your rights</h2>
        <p>
            You have the right to access your data, to have it corrected or
            deleted, to restrict or object to its processing, and to receive it
            in a portable format. To use any of these rights, including deleting
            your account,
            <template v-if="operator.email">
                email
                <a :href="`mailto:${operator.email}`">{{ operator.email }}</a>
            </template>
            <template v-else>contact the operator</template>
            from an address you can show is yours, or say which GitHub or
            Discord account is yours so it can be confirmed. You may also
            complain to the data protection authority where you live.
        </p>

        <h2>Children</h2>
        <p>
            You must be at least 13 to make an account. Where your country sets
            a higher age for consenting to online services (up to 16 in the EU),
            you need a parent's permission until you reach it. See the
            <Link :href="terms()">terms of use</Link>.
        </p>

        <h2>Changes</h2>
        <p>
            This policy changes when the site does. The date at the top shows
            the latest version.
        </p>
    </LegalPage>
</template>
