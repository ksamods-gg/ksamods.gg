<script setup lang="ts">
import { computed } from 'vue';
import { t } from '@/lib/i18n';
import { create as listingCreate } from '@/routes/listings';
import { create as shareCreate } from '@/routes/shares';

/**
 * The tabs of the one publish page: a mod, or a vehicle or save. Each keeps its own URL. Plain
 * links, since a mod's form may go on to GitHub to ask for pull request access.
 */
defineProps<{ current: 'mod' | 'share' }>();

const tabs = computed(
    () =>
        [
            { key: 'mod', label: t('A mod'), href: listingCreate().url },
            {
                key: 'share',
                label: t('A vehicle or save'),
                href: shareCreate().url,
            },
        ] as const,
);
</script>

<template>
    <nav
        class="mb-6 flex gap-6 text-sm shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_#3E3E3A]"
        :aria-label="$t('What to publish')"
    >
        <a
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
            >{{ tab.label }}</a
        >
    </nav>
</template>
