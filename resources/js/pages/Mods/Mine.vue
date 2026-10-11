<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import { computed } from 'vue';
import ModCard from '@/components/ModCard.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { create, edit } from '@/routes/listings';
import type { ModSummary } from '@/types';

type Role = 'owner' | 'admin' | 'topic' | 'marker';

const props = defineProps<{ mods: (ModSummary & { role: Role })[] }>();

const owned = computed(() => props.mods.filter((mod) => mod.role === 'owner'));
const managed = computed(() =>
    props.mods.filter((mod) => mod.role !== 'owner'),
);

// Why a mod the user does not own is theirs to manage.
const reasons: Record<Exclude<Role, 'owner'>, string> = {
    admin: "You're an admin of its repository",
    topic: "Its repository's ksa-index topic names you",
    marker: "Its repository's marker file names you",
};

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <Head title="My mods" />

    <AppLayout>
        <div class="mb-6 flex flex-wrap items-end justify-between gap-4">
            <div>
                <h1 class="text-2xl font-semibold tracking-tight">My mods</h1>
                <p :class="['mt-1 text-sm', muted]">
                    Listings whose release repository you control on GitHub,
                    which makes their listings yours to edit.
                </p>
            </div>
            <a
                :href="create().url"
                class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
            >
                Submit a mod
            </a>
        </div>

        <p
            v-if="mods.length === 0"
            :class="[
                'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            None of the listed mods' release repositories are yours on GitHub
            yet. If you maintain one in an organization, ask an admin to add the
            topic that names you, or submit your mod.
        </p>

        <template
            v-for="group in [
                { title: 'Your mods', mods: owned },
                { title: 'Mods you can manage', mods: managed },
            ]"
            :key="group.title"
        >
            <section v-if="group.mods.length" class="mb-10">
                <h2 class="mb-3 font-semibold">{{ group.title }}</h2>
                <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <li
                        v-for="mod in group.mods"
                        :key="mod.id"
                        class="flex flex-col gap-2"
                    >
                        <ModCard :mod="mod" class="flex-1" />
                        <div
                            class="flex items-center justify-between gap-3 px-1 text-xs"
                        >
                            <span :class="muted">{{
                                mod.role === 'owner' ? '' : reasons[mod.role]
                            }}</span>
                            <!-- A plain link: the edit page checks ownership again on GitHub. -->
                            <a
                                :href="edit(mod.id).url"
                                class="shrink-0 underline underline-offset-4"
                            >
                                Edit listing
                            </a>
                        </div>
                    </li>
                </ul>
            </section>
        </template>
    </AppLayout>
</template>
