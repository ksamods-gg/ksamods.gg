<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { show } from '@/routes/mods';

/**
 * Mods related to this one, in labelled groups: what it needs (Dependencies) and what needs it
 * (Required by). Each row is the mod, linked when the index lists it, and the versions involved.
 */
defineProps<{
    groups: {
        label: string;
        items: {
            id: string;
            // Null when the index does not list the mod.
            name: string | null;
            icon: string | null;
            min: string | null;
            max: string | null;
        }[];
    }[];
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
                    v-for="item in group.items"
                    :key="item.id"
                    class="flex items-center gap-3 py-2 text-sm"
                >
                    <ModIcon
                        :name="item.name ?? item.id"
                        :src="item.icon"
                        class="size-8 shrink-0 rounded-md text-xs"
                    />
                    <span class="min-w-0 flex-1 truncate">
                        <Link
                            v-if="item.name"
                            :href="show(item.id)"
                            class="font-medium hover:underline"
                            >{{ item.name }}</Link
                        >
                        <template v-else>
                            {{ item.id }}
                            <span :class="['text-xs', muted]"
                                >not in the index</span
                            >
                        </template>
                    </span>
                    <span :class="['shrink-0 text-xs', muted]">{{
                        versionRange(item.min, item.max)
                    }}</span>
                </li>
            </ul>
        </section>
    </div>
</template>
