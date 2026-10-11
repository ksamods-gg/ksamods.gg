<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import { useEchoNotification } from '@laravel/echo-vue';
import { ref, watch } from 'vue';
import { notificationChannel, notificationText } from '@/lib/notifications';
import {
    accounts,
    home,
    login,
    logout,
    notifications as notificationsPage,
} from '@/routes';
import { create } from '@/routes/listings';
import { index, mine } from '@/routes/mods';
import { open as openNotification } from '@/routes/notifications';
import type { NotificationData } from '@/types';

const page = usePage();

// The bell's count, kept current over Reverb between page loads.
const unread = ref(page.props.unreadNotifications);
watch(
    () => page.props.unreadNotifications,
    (count) => (unread.value = count),
);

// The newest notification, shown briefly in a corner as it arrives.
const toast = ref<(NotificationData & { id: string }) | null>(null);
let hideToast: ReturnType<typeof setTimeout> | undefined;

if (page.props.auth.user) {
    useEchoNotification<NotificationData>(
        notificationChannel(page.props.auth.user.id),
        (notification) => {
            unread.value++;
            toast.value = notification;
            clearTimeout(hideToast);
            hideToast = setTimeout(() => (toast.value = null), 6000);
        },
    );
}
</script>

<template>
    <div
        class="min-h-screen bg-[#FDFDFC] text-[#1b1b18] dark:bg-[#0a0a0a] dark:text-[#EDEDEC]"
    >
        <header class="border-b border-[#19140014] dark:border-[#3E3E3A]/60">
            <div
                class="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-3 sm:px-6"
            >
                <Link
                    :href="home()"
                    class="flex items-center gap-2 font-semibold tracking-tight"
                >
                    <img
                        src="/favicon.svg"
                        alt=""
                        width="24"
                        height="24"
                        class="size-6"
                    />
                    {{ page.props.name }}
                </Link>

                <!-- On the narrowest phones this wraps below the site name rather than overflowing. -->
                <div
                    class="ml-auto flex flex-wrap items-center justify-end gap-x-3 gap-y-2 text-sm"
                >
                    <Link :href="index()" class="hover:underline">Mods</Link>
                    <!-- A plain link: it may go on to GitHub to ask for pull request access. -->
                    <a :href="create().url" class="hover:underline">
                        Publish a mod
                    </a>
                    <template v-if="page.props.auth.user">
                        <Link :href="mine()" class="hover:underline">
                            My mods
                        </Link>
                        <Link
                            :href="notificationsPage()"
                            class="relative rounded-md p-1 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            :aria-label="
                                unread
                                    ? `Notifications, ${unread} unread`
                                    : 'Notifications'
                            "
                        >
                            <svg
                                viewBox="0 0 24 24"
                                fill="none"
                                stroke="currentColor"
                                stroke-width="1.75"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                                aria-hidden="true"
                                class="size-5"
                            >
                                <path
                                    d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0"
                                />
                            </svg>
                            <span
                                v-if="unread"
                                aria-hidden="true"
                                class="absolute -top-1 -right-1 min-w-4 rounded-full bg-accent px-1 text-center text-[10px] leading-4 font-semibold text-white dark:text-[#0a0a0a]"
                                >{{ unread > 99 ? '99+' : unread }}</span
                            >
                        </Link>
                        <Link
                            :href="accounts()"
                            class="flex items-center gap-2 hover:underline"
                        >
                            <img
                                v-if="page.props.auth.user.avatar"
                                :src="page.props.auth.user.avatar"
                                alt=""
                                class="hidden size-7 rounded-full sm:block"
                            />
                            <span class="hidden sm:inline">{{
                                page.props.auth.user.name
                            }}</span>
                            <span class="sm:hidden">Account</span>
                        </Link>
                        <Link
                            :href="logout()"
                            as="button"
                            class="rounded-md border border-[#19140035] px-3 py-1.5 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                        >
                            Log out
                        </Link>
                    </template>
                    <Link
                        v-else
                        :href="login()"
                        class="rounded-md border border-[#19140035] px-3 py-1.5 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                    >
                        Log in
                    </Link>
                </div>
            </div>
        </header>

        <main class="mx-auto max-w-6xl px-4 py-8 sm:px-6">
            <slot />
        </main>

        <div
            role="status"
            aria-live="polite"
            class="pointer-events-none fixed right-4 bottom-[calc(1rem+env(safe-area-inset-bottom,0px))] left-4 flex justify-end"
        >
            <!-- A plain link, so the browser keeps the #comment anchor through the redirect. -->
            <a
                v-if="toast"
                :href="openNotification(toast.id).url"
                class="pointer-events-auto w-full max-w-sm rounded-lg bg-white p-4 text-sm shadow-[0_8px_24px_rgba(0,0,0,0.12),inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[0_8px_24px_rgba(0,0,0,0.5),inset_0px_0px_0px_1px_#fffaed2d]"
            >
                <p class="font-medium">{{ notificationText(toast) }}</p>
                <p
                    class="mt-0.5 line-clamp-2 break-words text-[#706f6c] dark:text-[#A1A09A]"
                >
                    {{ toast.excerpt }}
                </p>
            </a>
        </div>
    </div>
</template>
