<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import ModCard from '@/components/ModCard.vue';
import ModIcon from '@/components/ModIcon.vue';
import PageMeta from '@/components/PageMeta.vue';
import ShareRow from '@/components/ShareRow.vue';
import Stars from '@/components/Stars.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import {
    BOREA_RELEASES_URL,
    boreaInstallLink,
    boreaSupported,
} from '@/lib/borea';
import { isMobileDevice } from '@/lib/device';
import { formatNumber } from '@/lib/format';
import { create } from '@/routes/listings';
import { index, show } from '@/routes/mods';
import { create as shareCreate, index as sharesIndex } from '@/routes/shares';
import type { ModSummary, ShareSummary } from '@/types';

type LogRelease = {
    mod: { id: string; name: string; icon: string | null };
    version: string;
    date: string;
    ago: string;
    // "Today", "Yesterday" or a date; the log groups releases by it.
    day: string;
    summary: string | null;
    download_url: string;
};

type CommunityPost = {
    key: string;
    rating: number | null;
    excerpt: string;
    author: { name: string; avatar: string | null };
    mod: { id: string; name: string };
    ago: string;
    url: string;
};

defineProps<{
    releases: LogRelease[];
    popular: ModSummary[];
    topRated: ModSummary[];
    // The newest vehicles and saves.
    shares: ShareSummary[];
    counts: {
        mods: number;
        downloads: number;
        vehicles: number;
        saves: number;
    };
    community: CommunityPost[];
}>();

const page = usePage();
const { userAgent } = page.props;
// Installing happens on a desktop; Borea runs on Windows and Linux.
const mobile = isMobileDevice(userAgent);
const borea = !mobile && boreaSupported(userAgent);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-xl bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const sectionHeading = 'text-xl font-semibold tracking-tight sm:text-2xl';
// The two paths in the first viewport, alike so neither outranks the other.
const pathTile =
    'flex flex-col px-5 py-4 transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.45)] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]';
const button =
    'inline-flex rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
const textLink = 'text-sm underline underline-offset-4 hover:text-accent';
</script>

<template>
    <PageMeta
        :title="$t('Kitten Space Agency mods')"
        :description="
            $t(
                'Find, install and publish Kitten Space Agency mods, and share vehicles and save games. Every mod in the KSA content index, with reviews and one-click installs through Borea.',
            )
        "
        :json-ld="{
            '@type': 'WebSite',
            name: page.props.name,
            url: `${page.props.appUrl}/`,
            potentialAction: {
                '@type': 'SearchAction',
                target: `${page.props.appUrl}/mods?q={search_term_string}`,
                'query-input': 'required name=search_term_string',
            },
        }"
    />

    <AppLayout>
        <!-- First viewport: two equal paths beside the launch log. -->
        <section
            class="grid items-start gap-10 pt-2 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:gap-14 lg:pt-10"
        >
            <div class="lg:pt-6">
                <h1
                    class="text-4xl leading-[1.05] font-semibold tracking-tight text-balance sm:text-5xl lg:text-[3.5rem]"
                >
                    {{
                        $t('Mods, vehicles and saves for Kitten Space Agency.')
                    }}
                </h1>
                <p
                    :class="[
                        'mt-5 max-w-xl text-lg leading-relaxed text-pretty',
                        muted,
                    ]"
                >
                    {{
                        $t(
                            'Every mod in the community KSA content index, with reviews, dependencies and one-click installs through Borea. Rockets, rovers and whole save games shared by players, with the mods each one needs.',
                        )
                    }}
                </p>

                <!-- Equal paths: getting things on top, sharing your own below; players and authors weigh the same. -->
                <div class="mt-8 grid gap-3 sm:grid-cols-2">
                    <Link :href="index()" :class="[card, pathTile]">
                        <span class="font-semibold">{{
                            $t('Browse :count mods', { count: counts.mods })
                        }}</span>
                        <span :class="['mt-0.5 text-sm', muted]">{{
                            $t('Search, read reviews, install')
                        }}</span>
                    </Link>
                    <Link
                        :href="sharesIndex('vehicles')"
                        :class="[card, pathTile]"
                    >
                        <span class="font-semibold">{{
                            $t('Vehicles & saves')
                        }}</span>
                        <span :class="['mt-0.5 text-sm', muted]">{{
                            $t(':vehicles vehicles, :saves saves', {
                                vehicles: counts.vehicles,
                                saves: counts.saves,
                            })
                        }}</span>
                    </Link>
                    <!-- A plain link: it may go on to GitHub to sign in. -->
                    <a :href="create().url" :class="[card, pathTile]">
                        <span class="font-semibold">{{
                            $t('Publish your mod')
                        }}</span>
                        <span :class="['mt-0.5 text-sm', muted]">{{
                            $t('List it in the index from GitHub')
                        }}</span>
                    </a>
                    <!-- A plain link: it may go on to sign in. -->
                    <a :href="shareCreate().url" :class="[card, pathTile]">
                        <span class="font-semibold">{{
                            $t('Share a vehicle or save')
                        }}</span>
                        <span :class="['mt-0.5 text-sm', muted]">{{
                            $t('Upload the zip, we host it')
                        }}</span>
                    </a>
                </div>

                <p :class="['mt-6 text-sm', muted]">
                    <span
                        class="font-medium text-[#1b1b18] tabular-nums dark:text-[#EDEDEC]"
                        >{{ formatNumber(counts.downloads) }}</span
                    >
                    {{
                        $t(
                            'downloads across :count mods, refreshed from the index every 10 minutes.',
                            { count: counts.mods },
                        )
                    }}
                </p>
            </div>

            <!-- The launch log: the newest stable releases, newest first, on a timeline rail. -->
            <div :class="[card, 'p-5 sm:p-6']">
                <div
                    class="mb-5 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1"
                >
                    <h2 class="font-semibold">{{ $t('Latest releases') }}</h2>
                    <p :class="['text-xs', muted]">
                        {{ $t('Stable releases from the KSA content index') }}
                    </p>
                </div>

                <ol v-if="releases.length" class="relative">
                    <!-- The rail, running the length of the log behind every node. -->
                    <span
                        aria-hidden="true"
                        class="absolute top-2 bottom-4 left-5 w-px bg-[#19140038] dark:bg-[#5a5a55]"
                    />
                    <template
                        v-for="(release, i) in releases"
                        :key="`${release.mod.id}-${release.version}`"
                    >
                        <!-- A node on the rail opens each day; today's in the accent. -->
                        <li
                            v-if="
                                i === 0 || release.day !== releases[i - 1].day
                            "
                            class="relative grid grid-cols-[2.5rem_minmax(0,1fr)] items-center gap-x-4 pb-3"
                        >
                            <span class="flex justify-center">
                                <span
                                    :class="[
                                        'size-2.5 rounded-full ring-4 ring-white dark:ring-[#161615]',
                                        i === 0
                                            ? 'bg-accent'
                                            : 'bg-[#1b1b18] dark:bg-[#EDEDEC]',
                                    ]"
                                />
                            </span>
                            <span :class="['text-xs font-medium', muted]">{{
                                release.day
                            }}</span>
                        </li>
                        <li
                            class="relative grid grid-cols-[2.5rem_minmax(0,1fr)] gap-x-4 pb-5 transition duration-700 ease-out last:pb-0 motion-reduce:transition-none sm:grid-cols-[2.5rem_minmax(0,1fr)_auto] starting:translate-y-2 starting:opacity-0"
                            :style="{ transitionDelay: `${i * 70}ms` }"
                        >
                            <ModIcon
                                :name="release.mod.name"
                                :src="release.mod.icon"
                                class="size-10 ring-4 ring-white dark:ring-[#161615]"
                            />
                            <div class="min-w-0">
                                <p
                                    class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5"
                                >
                                    <Link
                                        :href="
                                            show(release.mod.id, {
                                                query: { tab: 'releases' },
                                            })
                                        "
                                        :class="[
                                            'hover:underline',
                                            i === 0
                                                ? 'text-lg font-semibold'
                                                : 'font-medium',
                                        ]"
                                        >{{ release.mod.name }}</Link
                                    >
                                    <span
                                        class="rounded bg-[#f4f3ef] px-1.5 py-0.5 font-mono text-xs tabular-nums dark:bg-[#232321]"
                                        :title="
                                            $t('Released :ago', {
                                                ago: release.ago,
                                            })
                                        "
                                        >{{ release.version }}</span
                                    >
                                    <!-- The day markers show when; the exact age stays for screen readers. -->
                                    <time
                                        :datetime="release.date"
                                        class="sr-only"
                                        >{{ release.ago }}</time
                                    >
                                </p>
                                <p
                                    v-if="release.summary"
                                    :class="[
                                        'mt-1 text-sm',
                                        i === 0
                                            ? 'line-clamp-3'
                                            : 'line-clamp-2 sm:line-clamp-1',
                                        muted,
                                    ]"
                                >
                                    {{ release.summary }}
                                </p>
                            </div>
                            <!-- Installing happens on a desktop: Borea where it runs, the archive elsewhere. -->
                            <a
                                v-if="
                                    borea &&
                                    boreaInstallLink(
                                        release.mod.id,
                                        release.version,
                                    )
                                "
                                :href="
                                    boreaInstallLink(
                                        release.mod.id,
                                        release.version,
                                    ) ?? undefined
                                "
                                class="col-start-2 mt-2 justify-self-start rounded-md bg-[#1b1b18] px-3 py-1.5 text-xs font-medium text-white hover:bg-black sm:col-start-auto sm:mt-0 sm:self-center dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                                :aria-label="
                                    $t('Install :name :version with Borea', {
                                        name: release.mod.name,
                                        version: release.version,
                                    })
                                "
                            >
                                {{ $t('Install') }}
                            </a>
                            <a
                                v-else-if="!mobile"
                                :href="release.download_url"
                                class="col-start-2 mt-2 justify-self-start rounded-md px-3 py-1.5 text-xs font-medium shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.2)] hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.45)] sm:col-start-auto sm:mt-0 sm:self-center dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
                                :aria-label="
                                    $t('Download :name :version', {
                                        name: release.mod.name,
                                        version: release.version,
                                    })
                                "
                            >
                                {{ $t('Download') }}
                            </a>
                        </li>
                    </template>
                </ol>
                <p v-else :class="['text-sm', muted]">
                    {{
                        $t(
                            'No releases yet. They show up here as the index stamps them.',
                        )
                    }}
                </p>

                <div
                    class="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-[#19140014] pt-4 text-sm dark:border-[#3E3E3A]/60"
                >
                    <Link :href="index()" :class="textLink">{{
                        $t('Browse every mod')
                    }}</Link>
                    <a
                        v-if="borea"
                        :href="BOREA_RELEASES_URL"
                        target="_blank"
                        rel="noopener noreferrer"
                        :class="['text-xs hover:underline', muted]"
                        >{{ $t('Install needs Borea. Get it on GitHub') }}</a
                    >
                    <span v-else-if="mobile" :class="['text-xs', muted]">{{
                        $t('Installs and downloads are desktop only')
                    }}</span>
                </div>
            </div>
        </section>

        <!-- What people install. -->
        <section v-if="popular.length" class="mt-24">
            <div class="mb-6 flex flex-wrap items-end justify-between gap-3">
                <h2 :class="sectionHeading">{{ $t('Most installed') }}</h2>
                <Link :href="index()" :class="textLink">{{
                    $t('All mods')
                }}</Link>
            </div>
            <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <!-- Three on phones, so the shelf does not become a long scroll. -->
                <li
                    v-for="(mod, i) in popular"
                    :key="mod.id"
                    :class="i >= 3 && 'hidden sm:block'"
                >
                    <ModCard :mod="mod" />
                </li>
            </ul>
        </section>

        <!-- What people rate highest, once they have rated anything. -->
        <section v-if="topRated.length" class="mt-20">
            <h2 :class="[sectionHeading, 'mb-6']">{{ $t('Highest rated') }}</h2>
            <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <li v-for="mod in topRated" :key="mod.id">
                    <ModCard :mod="mod" />
                </li>
            </ul>
        </section>

        <!-- What players share: the newest vehicles and saves, and how to share yours. -->
        <section class="mt-20">
            <div class="mb-6 flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h2 :class="sectionHeading">
                        {{ $t('New vehicles and saves') }}
                    </h2>
                    <p :class="['mt-1 max-w-prose text-sm', muted]">
                        {{
                            $t(
                                'Hosted here, checked on upload, listed in the KSA content index and copied to the Internet Archive.',
                            )
                        }}
                    </p>
                </div>
                <div class="flex flex-wrap gap-x-5 gap-y-2">
                    <Link :href="sharesIndex('vehicles')" :class="textLink">{{
                        $t('All vehicles')
                    }}</Link>
                    <Link :href="sharesIndex('saves')" :class="textLink">{{
                        $t('All saves')
                    }}</Link>
                </div>
            </div>
            <ul v-if="shares.length" class="grid gap-3 lg:grid-cols-2">
                <li v-for="share in shares" :key="`${share.type}-${share.id}`">
                    <ShareRow :share="share" />
                </li>
            </ul>
            <p v-else :class="muted">
                {{ $t('Nothing shared yet. Be the first.') }}
            </p>
            <!-- A plain link: it may go on to sign in. -->
            <a :href="shareCreate().url" :class="[button, 'mt-6']">
                {{ $t('Share a vehicle or save') }}
            </a>
        </section>

        <!-- How each path works, side by side: installing and publishing. -->
        <section class="mt-24 grid gap-4 lg:grid-cols-2">
            <div :class="[card, 'p-6 sm:p-8']">
                <h2 :class="sectionHeading">
                    {{ $t('Install in one click') }}
                </h2>
                <p :class="['mt-2 max-w-prose', muted]">
                    {{
                        $t(
                            'Borea, the KSA content manager from KSAModding, takes the install links on every mod page.',
                        )
                    }}
                </p>
                <ol class="mt-6 flex flex-col gap-4">
                    <li
                        v-for="(step, i) in [
                            $t('Get Borea for Windows or Linux.'),
                            $t(
                                'Pick a mod here and choose Install with Borea.',
                            ),
                            $t(
                                'Borea opens and installs the release you picked.',
                            ),
                        ]"
                        :key="step"
                        class="flex gap-3"
                    >
                        <span
                            aria-hidden="true"
                            class="flex size-7 shrink-0 items-center justify-center rounded-full bg-[#f4f3ef] text-xs font-semibold tabular-nums dark:bg-[#232321]"
                            >{{ i + 1 }}</span
                        >
                        <span class="pt-0.5">{{ step }}</span>
                    </li>
                </ol>
                <p :class="['mt-6 text-sm', muted]">
                    {{
                        $t(
                            'On macOS, every release has a direct download. On a phone, browse now and install later on your computer.',
                        )
                    }}
                </p>
                <a
                    :href="BOREA_RELEASES_URL"
                    target="_blank"
                    rel="noopener noreferrer"
                    :class="[button, 'mt-8']"
                >
                    {{ $t('Get Borea on GitHub') }}
                </a>
            </div>

            <div :class="[card, 'p-6 sm:p-8']">
                <h2 :class="sectionHeading">
                    {{ $t('Publish without the TOML') }}
                </h2>
                <p :class="['mt-2 max-w-prose', muted]">
                    {{
                        $t(
                            'Listings live in the KSA content index. ksamods.gg writes yours and opens the pull request from your GitHub account, so it merges as yours.',
                        )
                    }}
                </p>
                <ol class="mt-6 flex flex-col gap-4">
                    <li
                        v-for="(step, i) in [
                            $t('Log in with GitHub.'),
                            $t('Fill in your mod’s listing.'),
                            $t(
                                'We open the pull request on the content index for you.',
                            ),
                            $t(
                                'Once it merges, your mod shows up here and in every client that reads the index.',
                            ),
                        ]"
                        :key="step"
                        class="flex gap-3"
                    >
                        <span
                            aria-hidden="true"
                            class="flex size-7 shrink-0 items-center justify-center rounded-full bg-[#f4f3ef] text-xs font-semibold tabular-nums dark:bg-[#232321]"
                            >{{ i + 1 }}</span
                        >
                        <span class="pt-0.5">{{ step }}</span>
                    </li>
                </ol>
                <!-- A plain link: it may go on to GitHub to sign in. -->
                <a :href="create().url" :class="[button, 'mt-8']">
                    {{ $t('Publish your mod') }}
                </a>
            </div>
        </section>

        <!-- What people are saying, across every mod. -->
        <section class="mt-24">
            <h2 :class="[sectionHeading, 'mb-6']">
                {{ $t('From the community') }}
            </h2>
            <!-- One dense list, a change of pace from the card shelves above. -->
            <ul
                v-if="community.length"
                :class="[
                    card,
                    'divide-y divide-[#19140014] overflow-hidden dark:divide-[#3E3E3A]/60',
                ]"
            >
                <li v-for="post in community" :key="post.key">
                    <!-- A plain link, so the browser keeps the #anchor to the post. -->
                    <a
                        :href="post.url"
                        class="flex gap-3 px-5 py-4 transition-colors hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                    >
                        <ModIcon
                            :name="post.author.name"
                            :src="post.author.avatar"
                            class="size-8 rounded-full text-sm"
                        />
                        <div class="min-w-0">
                            <p class="text-sm">
                                <span class="font-medium">{{
                                    post.author.name
                                }}</span>
                                <span :class="muted">
                                    {{ $t('on :mod', { mod: post.mod.name }) }}
                                    · {{ post.ago }}</span
                                >
                            </p>
                            <Stars
                                v-if="post.rating !== null"
                                :rating="post.rating"
                                class="mt-1"
                            />
                            <p
                                class="mt-1 line-clamp-2 text-sm break-words whitespace-pre-line"
                            >
                                {{ post.excerpt }}
                            </p>
                        </div>
                    </a>
                </li>
            </ul>
            <p v-else :class="muted">
                {{
                    $t('Reviews and comments from every mod page show up here.')
                }}
                <Link :href="index()" :class="textLink">{{
                    $t('Find a mod you use and say how it went')
                }}</Link
                >.
            </p>
        </section>

        <!-- The close: credit where the data and the installer come from. -->
        <section
            class="mt-24 border-t border-[#19140014] pt-10 dark:border-[#3E3E3A]/60"
        >
            <div
                class="grid gap-6 md:grid-cols-[minmax(0,1fr)_auto] md:items-end"
            >
                <div>
                    <h2 :class="sectionHeading">
                        {{ $t('Built on the KSAModding community’s work') }}
                    </h2>
                    <p :class="['mt-2 max-w-2xl', muted]">
                        {{
                            $t(
                                'ksamods.gg is an independent site. Every mod listing comes from KSAModding’s content index, and installs go through their Borea. Vehicles and saves are hosted here and listed in the same index.',
                            )
                        }}
                    </p>
                </div>
                <div class="flex flex-wrap gap-x-5 gap-y-2">
                    <a
                        href="https://github.com/KSAModding/content-index"
                        target="_blank"
                        rel="noopener noreferrer"
                        :class="textLink"
                        >{{ $t('The content index') }}</a
                    >
                    <a
                        href="https://github.com/KSAModding/Borea"
                        target="_blank"
                        rel="noopener noreferrer"
                        :class="textLink"
                        >Borea</a
                    >
                </div>
            </div>
        </section>
    </AppLayout>
</template>
