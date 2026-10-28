<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { show } from '@/routes/mods';
import type { RelatedMod } from '@/types';

/**
 * Mods related to this one, in labelled groups: what it needs (Dependencies) and what needs it
 * (Required by). Each row is the mod, linked when the index lists it, and the versions involved.
 * A row with alternatives (any_of) lists them, any one of which will do.
 */
defineProps<{
    groups: { label: string; items: RelatedMod[] }[];
}>();

function versionRange(min: string | null, max: string | null): string {
    if (min && max) {
        return `${min} to ${max}`;
    }

    if (min) {
        return `${min} or newer`;
    }

    return max ? `up to ${max}` : 'any version';
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <div class="flex flex-col gap-5">
        <section v-for="group in groups" :key="group.label">
            <h3 class="mb-1 text-sm font-semibold">{{ group.label }}</h3>
            <ul class="divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60">
                <li
                    v-for="(item, index) in group.items"
                    :key="item.id ?? `any-of-${index}`"
                    class="py-2 text-sm"
                >
                    <p v-if="item.any_of" :class="['mb-1 text-xs', muted]">
                        One of these:
                    </p>
                    <div
                        v-for="mod in item.any_of ?? [item]"
                        :key="mod.id ?? ''"
                        :class="[
                            'flex items-center gap-3',
                            item.any_of ? 'py-1 pl-3' : '',
                        ]"
                    >
                        <ModIcon
                            :name="mod.name ?? mod.id ?? '?'"
                            :src="mod.icon"
                            class="size-8 shrink-0 rounded-md text-xs"
                        />
                        <span class="min-w-0 flex-1 truncate">
                            <Link
                                v-if="mod.name && mod.id"
                                :href="show(mod.id)"
                                class="font-medium hover:underline"
                                >{{ mod.name }}</Link
                            >
                            <template v-else>
                                {{ mod.id }}
                                <span :class="['text-xs', muted]"
                                    >not in the index</span
                                >
                            </template>
                        </span>
                        <span :class="['shrink-0 text-xs', muted]">{{
                            versionRange(mod.min, mod.max)
                        }}</span>
                    </div>
                </li>
            </ul>
        </section>
    </div>
</template>
