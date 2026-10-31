<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import ModIcon from '@/components/ModIcon.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { accounts } from '@/routes';
import { mine } from '@/routes/mods';
import { notifications, profile } from '@/routes/settings';
import { create as shareCreate } from '@/routes/shares';

/**
 * The settings pages, laid out like GitHub's: who you are at the top, a grouped sidebar, and the
 * page's sections beside it.
 */
const page = usePage();
const user = computed(() => page.props.auth.user!);

const groups = [
    {
        title: null,
        items: [
            { label: 'Public profile', href: profile().url, icon: 'card' },
            { label: 'Account', href: accounts().url, icon: 'person' },
            { label: 'Notifications', href: notifications().url, icon: 'bell' },
        ],
    },
    {
        title: 'Your content',
        items: [
            { label: 'My mods', href: mine().url, icon: 'box' },
            {
                label: 'Share a vehicle or save',
                href: shareCreate().url,
                icon: 'upload',
            },
        ],
    },
] as const;

const current = computed(() => page.url.split('?')[0]);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <AppLayout>
        <div class="mx-auto max-w-5xl">
            <div class="mb-8 flex items-center gap-3">
                <img
                    v-if="user.avatar"
                    :src="user.avatar"
                    alt=""
                    class="size-12 rounded-full"
                />
                <ModIcon
                    v-else
                    :name="user.name"
                    :src="null"
                    class="size-12 rounded-full text-lg"
                />
                <div>
                    <p class="text-xl font-semibold tracking-tight">
                        {{ user.name }}
                    </p>
                    <p :class="['text-sm', muted]">Your personal account</p>
                </div>
            </div>

            <div class="grid gap-8 md:grid-cols-[14rem_minmax(0,1fr)]">
                <nav aria-label="Settings" class="flex flex-col gap-4">
                    <div v-for="(group, index) in groups" :key="index">
                        <p
                            v-if="group.title"
                            :class="['mb-1 px-3 text-xs font-medium', muted]"
                        >
                            {{ group.title }}
                        </p>
                        <ul class="flex flex-col gap-0.5">
                            <li v-for="item in group.items" :key="item.href">
                                <Link
                                    :href="item.href"
                                    :aria-current="
                                        current === item.href
                                            ? 'page'
                                            : undefined
                                    "
                                    :class="[
                                        'relative flex items-center gap-2.5 rounded-md px-3 py-1.5 text-sm hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]',
                                        current === item.href
                                            ? 'bg-[#f4f3ef] font-semibold before:absolute before:top-1 before:bottom-1 before:-left-2 before:w-1 before:rounded-full before:bg-accent dark:bg-[#232321]'
                                            : '',
                                    ]"
                                >
                                    <svg
                                        viewBox="0 0 24 24"
                                        fill="none"
                                        stroke="currentColor"
                                        stroke-width="1.75"
                                        stroke-linecap="round"
                                        stroke-linejoin="round"
                                        aria-hidden="true"
                                        :class="['size-4 shrink-0', muted]"
                                    >
                                        <template v-if="item.icon === 'card'">
                                            <rect
                                                x="3"
                                                y="5"
                                                width="18"
                                                height="14"
                                                rx="2"
                                            />
                                            <circle cx="9" cy="11" r="2" />
                                            <path
                                                d="M6 16a3 3 0 0 1 6 0M15 10h3M15 14h3"
                                            />
                                        </template>
                                        <template
                                            v-else-if="item.icon === 'person'"
                                        >
                                            <circle cx="12" cy="8" r="4" />
                                            <path d="M4 21a8 8 0 0 1 16 0" />
                                        </template>
                                        <path
                                            v-else-if="item.icon === 'bell'"
                                            d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0"
                                        />
                                        <template
                                            v-else-if="item.icon === 'box'"
                                        >
                                            <path
                                                d="M21 8 12 3 3 8v8l9 5 9-5V8Z"
                                            />
                                            <path d="m3 8 9 5 9-5M12 13v8" />
                                        </template>
                                        <template v-else>
                                            <path
                                                d="M12 16V4m0 0L7 9m5-5 5 5"
                                            />
                                            <path d="M4 20h16" />
                                        </template>
                                    </svg>
                                    {{ item.label }}
                                </Link>
                            </li>
                        </ul>
                    </div>
                </nav>

                <div class="flex min-w-0 flex-col gap-10">
                    <slot />
                </div>
            </div>
        </div>
    </AppLayout>
</template>
