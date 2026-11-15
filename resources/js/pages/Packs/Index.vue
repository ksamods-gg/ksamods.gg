<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import BrowseTabs from '@/components/BrowseTabs.vue';
import ModIcon from '@/components/ModIcon.vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { show } from '@/routes/packs';
import type { PackSummary } from '@/types';

defineProps<{ packs: PackSummary[] }>();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const chip =
    'inline-flex items-center rounded-full bg-[#f4f3ef] px-2 py-0.5 text-xs dark:bg-[#232321]';
</script>

<template>
    <PageMeta
        :title="$t('Kitten Space Agency mod packs')"
        :description="
            $t(
                'Sets of Kitten Space Agency mods at versions that work together, installed in one go with Borea.',
            )
        "
    />

    <AppLayout>
        <BrowseTabs current="packs" />

        <h1 class="text-2xl font-semibold tracking-tight">
            {{ $t('Mod packs for Kitten Space Agency') }}
        </h1>
        <p :class="['mt-1 text-sm', muted]">
            {{
                $t(
                    'Sets of mods at versions that work together, from the community content index. Borea installs a pack with everything its mods need.',
                )
            }}
        </p>

        <ul v-if="packs.length" class="mt-6 flex flex-col gap-3">
            <li v-for="pack in packs" :key="pack.id">
                <article
                    class="relative grid grid-cols-[3.5rem_minmax(0,1fr)] gap-x-4 gap-y-2 rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] sm:grid-cols-[5rem_minmax(0,1fr)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
                >
                    <ModIcon
                        :name="pack.name"
                        :src="pack.icon"
                        class="size-14 rounded-xl text-xl sm:row-span-2 sm:size-20 sm:text-2xl"
                    />
                    <div class="min-w-0">
                        <h2 class="flex flex-wrap items-baseline gap-x-2">
                            <!-- The whole row opens the pack; the name is the link. -->
                            <Link
                                :href="show(pack.id)"
                                class="text-base font-semibold after:absolute after:inset-0 after:rounded-lg hover:underline"
                                >{{ pack.name }}</Link
                            >
                            <span
                                v-if="pack.authors.length"
                                :class="['text-sm font-normal', muted]"
                                >{{
                                    $t('by :authors', {
                                        authors: pack.authors.join(', '),
                                    })
                                }}</span
                            >
                        </h2>
                        <p
                            v-if="pack.abstract"
                            class="mt-1 line-clamp-2 text-sm text-[#4a4a46] dark:text-[#c4c3be]"
                        >
                            {{ pack.abstract }}
                        </p>
                    </div>
                    <ul
                        class="col-span-2 flex flex-wrap items-center gap-1.5 sm:col-span-1 sm:col-start-2"
                        :aria-label="$t('Details')"
                    >
                        <li :class="[chip, 'text-accent']">
                            {{
                                pack.mod_count === 1
                                    ? $t('1 mod')
                                    : $t(':count mods', {
                                          count: pack.mod_count,
                                      })
                            }}
                        </li>
                        <li :class="[chip, 'font-mono tabular-nums']">
                            {{ pack.version }}
                        </li>
                        <li v-if="pack.game_min" :class="chip">
                            {{
                                $t('Game :version or newer', {
                                    version: pack.game_min,
                                })
                            }}
                        </li>
                        <li v-if="pack.updated_ago" :class="['text-xs', muted]">
                            {{ $t('Updated :ago', { ago: pack.updated_ago }) }}
                        </li>
                    </ul>
                </article>
            </li>
        </ul>
        <p
            v-else
            :class="[
                'mt-6 rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            {{ $t('The index lists no mod packs yet.') }}
        </p>
    </AppLayout>
</template>
