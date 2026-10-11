<script setup lang="ts">
import { Head, Link, router, usePage } from '@inertiajs/vue3';
import { useEchoNotification } from '@laravel/echo-vue';
import { computed } from 'vue';
import ModIcon from '@/components/ModIcon.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate } from '@/lib/format';
import { notificationChannel, notificationText } from '@/lib/notifications';
import { open, readAll } from '@/routes/notifications';
import type { AppNotification } from '@/types';

const props = defineProps<{ notifications: AppNotification[] }>();

const page = usePage();
const hasUnread = computed(() => props.notifications.some((n) => !n.read));

// A new one shows up here as it arrives.
useEchoNotification(notificationChannel(page.props.auth.user!.id), () =>
    router.reload({ only: ['notifications', 'unreadNotifications'] }),
);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <Head title="Notifications" />

    <AppLayout>
        <div class="mx-auto max-w-2xl">
            <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
                <h1 class="text-2xl font-semibold tracking-tight">
                    Notifications
                </h1>
                <Link
                    v-if="hasUnread"
                    :href="readAll()"
                    as="button"
                    preserve-scroll
                    :class="['text-sm hover:underline', muted]"
                >
                    Mark all as read
                </Link>
            </div>

            <ul
                v-if="notifications.length"
                class="divide-y divide-[#19140014] rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:divide-[#3E3E3A]/60 dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
            >
                <li
                    v-for="notification in notifications"
                    :key="notification.id"
                >
                    <!-- A plain link, so the browser keeps the #comment anchor through the redirect. -->
                    <a
                        :href="open(notification.id).url"
                        class="flex gap-3 p-4 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                    >
                        <ModIcon
                            :name="notification.data.actor.name"
                            :src="notification.data.actor.avatar"
                            class="size-8 rounded-full text-sm"
                        />
                        <div class="min-w-0 flex-1">
                            <p
                                :class="[
                                    'text-sm',
                                    notification.read ? muted : 'font-medium',
                                ]"
                            >
                                {{ notificationText(notification.data) }}
                            </p>
                            <p
                                :class="[
                                    'mt-0.5 line-clamp-2 text-sm break-words',
                                    muted,
                                ]"
                            >
                                {{ notification.data.excerpt }}
                            </p>
                            <time
                                :datetime="notification.created_at"
                                :class="['mt-1 block text-xs', muted]"
                                >{{ formatDate(notification.created_at) }}</time
                            >
                        </div>
                        <span
                            v-if="!notification.read"
                            class="mt-1.5 size-2 shrink-0 rounded-full bg-[#f53003] dark:bg-[#FF4433]"
                        >
                            <span class="sr-only">Unread</span>
                        </span>
                    </a>
                </li>
            </ul>
            <p
                v-else
                :class="[
                    'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                    muted,
                ]"
            >
                Nothing yet. You'll hear here when someone replies to you or
                mentions you with @.
            </p>
        </div>
    </AppLayout>
</template>
