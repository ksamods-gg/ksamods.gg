<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import { dashboard, posts, reports, users } from '@/routes/admin';
import shares from '@/routes/admin/shares';
import { t } from '@/lib/i18n';

/**
 * The admin panel's tabs, at the top of each admin page.
 */
const page = usePage();
const openReports = computed(() => Number(page.props.openReports ?? 0));

const tabs = computed(() => [
    { label: t('Dashboard'), href: dashboard().url },
    { label: t('Reports'), href: reports().url, count: openReports.value },
    { label: t('Reviews'), href: posts('reviews').url },
    { label: t('Comments'), href: posts('comments').url },
    { label: t('Users'), href: users().url },
    // The share host's fixes are for admins.
    ...(page.props.auth.user?.role === 'admin'
        ? [{ label: t('Share host'), href: shares.index().url }]
        : []),
]);

// The current tab: the longest tab address the page's address starts with.
const current = computed(() => {
    const path = page.url.split('?')[0];

    return tabs.value
        .filter((tab) => path === tab.href || path.startsWith(`${tab.href}/`))
        .sort((a, b) => b.href.length - a.href.length)[0]?.href;
});

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <div>
        <p :class="['text-sm', muted]">{{ $t('Admin') }}</p>
        <nav
            :aria-label="$t('Admin')"
            class="mt-2 mb-6 flex [scrollbar-width:none] gap-6 overflow-x-auto text-sm shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_#3E3E3A]"
        >
            <Link
                v-for="tab in tabs"
                :key="tab.href"
                :href="tab.href"
                :aria-current="tab.href === current ? 'page' : undefined"
                :class="[
                    'flex shrink-0 items-center gap-1.5 border-b-2 pb-2 font-medium',
                    tab.href === current
                        ? 'border-[#1b1b18] dark:border-[#EDEDEC]'
                        : [
                              'border-transparent hover:text-[#1b1b18] dark:hover:text-[#EDEDEC]',
                              muted,
                          ],
                ]"
            >
                {{ tab.label }}
                <span
                    v-if="tab.count"
                    class="rounded-full bg-accent px-1.5 text-xs leading-5 font-semibold text-white tabular-nums dark:text-[#0a0a0a]"
                    >{{ tab.count
                    }}<span class="sr-only"> {{ $t('open') }}</span></span
                >
            </Link>
        </nav>
    </div>
</template>
