<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import { computed } from 'vue';
import { t } from '@/lib/i18n';
import { index as modsIndex } from '@/routes/mods';
import { index as sharesIndex } from '@/routes/shares';

/**
 * The tabs of the one browse page: mods, vehicles and saves. Each tab keeps its own URL, so
 * links and search results to /mods, /vehicles and /saves still land on the right tab.
 */
defineProps<{ current: 'mods' | 'vehicles' | 'saves' }>();

const tabs = computed(
    () =>
        [
            { key: 'mods', label: t('Mods'), href: modsIndex() },
            {
                key: 'vehicles',
                label: t('Vehicles'),
                href: sharesIndex('vehicles'),
            },
            { key: 'saves', label: t('Saves'), href: sharesIndex('saves') },
        ] as const,
);
</script>

<template>
    <nav
        class="mb-6 flex gap-6 text-sm shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_#3E3E3A]"
        :aria-label="$t('Browse')"
    >
        <Link
            v-for="tab in tabs"
            :key="tab.key"
            :href="tab.href"
            :aria-current="tab.key === current ? 'page' : undefined"
            :class="[
                'border-b-2 pb-2 font-medium',
                tab.key === current
                    ? 'border-[#1b1b18] dark:border-[#EDEDEC]'
                    : 'border-transparent text-[#706f6c] hover:text-[#1b1b18] dark:text-[#A1A09A] dark:hover:text-[#EDEDEC]',
            ]"
            >{{ tab.label }}</Link
        >
    </nav>
</template>
