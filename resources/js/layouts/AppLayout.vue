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
    policy,
    privacy,
    terms,
} from '@/routes';
import { dashboard as adminDashboard } from '@/routes/admin';
import { create } from '@/routes/listings';
import { index, mine } from '@/routes/mods';
import { open as openNotification } from '@/routes/notifications';
import { index as sharesIndex } from '@/routes/shares';
import type { NotificationData } from '@/types';

const page = usePage();

// The phone menu, closed again whenever a link in it is followed.
const menuOpen = ref(false);
watch(
    () => page.url,
    () => (menuOpen.value = false),
);

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
        class="flex min-h-screen flex-col bg-[#FDFDFC] text-[#1b1b18] dark:bg-[#0a0a0a] dark:text-[#EDEDEC]"
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

                <!-- From sm the links sit in the bar; on phones they fold into the menu below, leaving the bell. -->
                <div
                    class="ml-auto flex items-center justify-end gap-x-3 text-sm"
                >
                    <div class="hidden sm:contents">
                        <Link :href="index()" class="hover:underline"
                            >Mods</Link
                        >
                        <Link
                            :href="sharesIndex('vehicles')"
                            class="hover:underline"
                            >Vehicles &amp; saves</Link
                        >
                        <!-- A plain link: it may go on to GitHub to ask for pull request access. -->
                        <a :href="create().url" class="hover:underline">
                            Publish a mod
                        </a>
                        <template v-if="page.props.auth.user">
                            <Link :href="mine()" class="hover:underline">
                                My mods
                            </Link>
                            <Link
                                v-if="page.props.auth.user.role"
                                :href="adminDashboard()"
                                class="flex items-center gap-1.5 hover:underline"
                            >
                                Admin
                                <span
                                    v-if="page.props.openReports"
                                    class="rounded-full bg-accent px-1.5 text-xs leading-5 font-semibold text-white tabular-nums dark:text-[#0a0a0a]"
                                    >{{ page.props.openReports
                                    }}<span class="sr-only">
                                        open reports</span
                                    ></span
                                >
                            </Link>
                        </template>
                    </div>
                    <Link
                        v-if="page.props.auth.user"
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
                    <div class="hidden sm:contents">
                        <template v-if="page.props.auth.user">
                            <Link
                                :href="accounts()"
                                class="flex items-center gap-2 hover:underline"
                            >
                                <img
                                    v-if="page.props.auth.user.avatar"
                                    :src="page.props.auth.user.avatar"
                                    alt=""
                                    class="size-7 rounded-full"
                                />
                                {{ page.props.auth.user.name }}
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
                    <button
                        type="button"
                        class="relative rounded-md p-1 hover:bg-[#f4f3ef] sm:hidden dark:hover:bg-[#1c1c1a]"
                        aria-controls="mobile-menu"
                        :aria-expanded="menuOpen"
                        :aria-label="menuOpen ? 'Close menu' : 'Open menu'"
                        @click="menuOpen = !menuOpen"
                    >
                        <svg
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.75"
                            stroke-linecap="round"
                            aria-hidden="true"
                            class="size-5"
                        >
                            <path v-if="menuOpen" d="M6 6l12 12M18 6 6 18" />
                            <path v-else d="M4 7h16M4 12h16M4 17h16" />
                        </svg>
                        <span
                            v-if="
                                !menuOpen &&
                                page.props.auth.user?.role &&
                                page.props.openReports
                            "
                            aria-hidden="true"
                            class="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-accent"
                        ></span>
                    </button>
                </div>
            </div>

            <nav
                v-if="menuOpen"
                id="mobile-menu"
                aria-label="Menu"
                class="border-t border-[#19140014] px-4 pb-4 text-sm sm:hidden dark:border-[#3E3E3A]/60"
                @keydown.esc="menuOpen = false"
            >
                <ul class="flex flex-col py-2">
                    <li>
                        <Link
                            :href="index()"
                            class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >Mods</Link
                        >
                    </li>
                    <li>
                        <Link
                            :href="sharesIndex('vehicles')"
                            class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >Vehicles &amp; saves</Link
                        >
                    </li>
                    <li>
                        <a
                            :href="create().url"
                            class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >Publish a mod</a
                        >
                    </li>
                    <template v-if="page.props.auth.user">
                        <li>
                            <Link
                                :href="mine()"
                                class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                                >My mods</Link
                            >
                        </li>
                        <li v-if="page.props.auth.user.role">
                            <Link
                                :href="adminDashboard()"
                                class="-mx-2 flex items-center gap-1.5 rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >
                                Admin
                                <span
                                    v-if="page.props.openReports"
                                    class="rounded-full bg-accent px-1.5 text-xs leading-5 font-semibold text-white tabular-nums dark:text-[#0a0a0a]"
                                    >{{ page.props.openReports
                                    }}<span class="sr-only">
                                        open reports</span
                                    ></span
                                >
                            </Link>
                        </li>
                        <li>
                            <Link
                                :href="accounts()"
                                class="-mx-2 flex items-center gap-2 rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >
                                <img
                                    v-if="page.props.auth.user.avatar"
                                    :src="page.props.auth.user.avatar"
                                    alt=""
                                    class="size-6 rounded-full"
                                />
                                {{ page.props.auth.user.name }}
                            </Link>
                        </li>
                    </template>
                </ul>
                <Link
                    v-if="page.props.auth.user"
                    :href="logout()"
                    as="button"
                    class="w-full rounded-md border border-[#19140035] px-3 py-2 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                >
                    Log out
                </Link>
                <Link
                    v-else
                    :href="login()"
                    class="block w-full rounded-md border border-[#19140035] px-3 py-2 text-center hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                >
                    Log in
                </Link>
            </nav>
        </header>

        <main class="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6">
            <slot />
        </main>

        <footer class="border-t border-[#19140014] dark:border-[#3E3E3A]/60">
            <div
                class="mx-auto flex max-w-6xl flex-wrap gap-x-4 gap-y-2 px-4 py-6 text-sm text-[#706f6c] sm:px-6 dark:text-[#A1A09A]"
            >
                <Link :href="privacy()" class="hover:underline">Privacy</Link>
                <Link :href="terms()" class="hover:underline">Terms</Link>
                <Link :href="policy()" class="hover:underline"
                    >Host policy</Link
                >
            </div>
        </footer>

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
