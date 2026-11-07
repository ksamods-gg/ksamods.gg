<script setup lang="ts">
import { Link, router, usePage } from '@inertiajs/vue3';
import { useEchoNotification, useEchoPublic } from '@laravel/echo-vue';
import { onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue';
import CookieConsent from '@/components/CookieConsent.vue';
import ModIcon from '@/components/ModIcon.vue';
import { consentOpen } from '@/lib/consent';
import { noteDownloads, type DownloadsChanged } from '@/lib/liveDownloads';
import {
    notificationChannel,
    notificationExcerpt,
    notificationText,
} from '@/lib/notifications';
import {
    accounts,
    home,
    locale as switchLocale,
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
import { show as profileShow } from '@/routes/profiles';
import { profile as profileSettings } from '@/routes/settings';
import { index as translateIndex } from '@/routes/translate';
import type { NotificationData } from '@/types';

const page = usePage();

// The phone menu and the account menu, closed again whenever a link in them is followed.
const menuOpen = ref(false);
const accountMenuOpen = ref(false);
watch(
    () => page.url,
    () => {
        menuOpen.value = false;
        accountMenuOpen.value = false;
    },
);

// The account menu also closes on Escape, which returns focus to its toggle, and on a click outside.
const accountMenu = useTemplateRef<HTMLElement>('accountMenu');
const accountMenuToggle =
    useTemplateRef<HTMLButtonElement>('accountMenuToggle');

function closeAccountMenu(refocus = false) {
    if (accountMenuOpen.value && refocus) {
        accountMenuToggle.value?.focus();
    }

    accountMenuOpen.value = false;
}

function closeOnOutsideClick(event: PointerEvent) {
    if (!accountMenu.value?.contains(event.target as Node)) {
        closeAccountMenu();
    }
}

onMounted(() => document.addEventListener('pointerdown', closeOnOutsideClick));
onBeforeUnmount(() =>
    document.removeEventListener('pointerdown', closeOnOutsideClick),
);

const menuItem =
    'block px-3 py-2 hover:bg-[#f4f3ef] focus-visible:bg-[#f4f3ef] focus-visible:outline-none dark:hover:bg-[#232321] dark:focus-visible:bg-[#232321]';

// The bell's count, kept current over Reverb between page loads.
const unread = ref(page.props.unreadNotifications);
watch(
    () => page.props.unreadNotifications,
    (count) => (unread.value = count),
);

// The newest notification, shown briefly in a corner as it arrives.
const toast = ref<(NotificationData & { id: string }) | null>(null);
let hideToast: ReturnType<typeof setTimeout> | undefined;

// Vehicle and save download counts, live on every page that shows them.
useEchoPublic<DownloadsChanged>('share-downloads', '.downloads', noteDownloads);

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
                        <!-- Mods, vehicles and saves are tabs of one browse page. -->
                        <Link :href="index()" class="hover:underline">{{
                            $t('Browse')
                        }}</Link>
                        <!-- A plain link: it may go on to GitHub to ask for pull request access. -->
                        <a :href="create().url" class="hover:underline">
                            {{ $t('Publish a mod') }}
                        </a>
                    </div>
                    <Link
                        v-if="page.props.auth.user"
                        :href="notificationsPage()"
                        class="relative rounded-md p-1 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                        :aria-label="
                            unread
                                ? $t('Notifications, :count unread', {
                                      count: unread,
                                  })
                                : $t('Notifications')
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
                        <!-- Everything about the player sits behind their avatar, as on GitHub. -->
                        <div
                            v-if="page.props.auth.user"
                            ref="accountMenu"
                            class="relative"
                            @keydown.escape="closeAccountMenu(true)"
                        >
                            <button
                                ref="accountMenuToggle"
                                type="button"
                                class="relative flex items-center gap-1 rounded-full p-0.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                                aria-controls="account-menu"
                                :aria-expanded="accountMenuOpen"
                                :aria-label="$t('Your account')"
                                @click="accountMenuOpen = !accountMenuOpen"
                            >
                                <img
                                    v-if="page.props.auth.user.avatar"
                                    :src="page.props.auth.user.avatar"
                                    alt=""
                                    class="size-7 rounded-full"
                                />
                                <ModIcon
                                    v-else
                                    :name="page.props.auth.user.name"
                                    :src="null"
                                    class="size-7 rounded-full text-xs"
                                />
                                <svg
                                    viewBox="0 0 16 16"
                                    fill="currentColor"
                                    aria-hidden="true"
                                    :class="[
                                        'size-3.5 transition-transform',
                                        accountMenuOpen && 'rotate-180',
                                    ]"
                                >
                                    <path
                                        d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z"
                                    />
                                </svg>
                                <!-- Moderators see waiting reports on the avatar too. -->
                                <span
                                    v-if="
                                        page.props.auth.user.role &&
                                        page.props.openReports
                                    "
                                    aria-hidden="true"
                                    class="absolute top-0 left-6 size-2 rounded-full bg-accent"
                                ></span>
                            </button>

                            <div
                                v-show="accountMenuOpen"
                                id="account-menu"
                                class="absolute top-full right-0 z-20 mt-2 w-60 rounded-md bg-white py-1 text-sm shadow-[0_8px_24px_rgba(0,0,0,0.12),inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[0_8px_24px_rgba(0,0,0,0.5),inset_0px_0px_0px_1px_#fffaed2d]"
                            >
                                <p
                                    class="truncate border-b border-[#19140014] px-3 pt-1.5 pb-2.5 dark:border-[#3E3E3A]/60"
                                >
                                    <span class="block font-medium">{{
                                        page.props.auth.user.name
                                    }}</span>
                                    <span
                                        v-if="page.props.auth.user.username"
                                        class="text-xs text-[#706f6c] dark:text-[#A1A09A]"
                                        >@{{
                                            page.props.auth.user.username
                                        }}</span
                                    >
                                </p>
                                <ul class="py-1">
                                    <li>
                                        <Link
                                            :href="
                                                page.props.auth.user
                                                    .profile_enabled &&
                                                page.props.auth.user.username
                                                    ? profileShow(
                                                          String(
                                                              page.props.auth
                                                                  .user
                                                                  .username,
                                                          ),
                                                      ).url
                                                    : profileSettings().url
                                            "
                                            :class="menuItem"
                                            >{{ $t('Your profile') }}</Link
                                        >
                                    </li>
                                    <li>
                                        <Link
                                            :href="mine()"
                                            :class="menuItem"
                                            >{{ $t('My mods') }}</Link
                                        >
                                    </li>
                                    <li>
                                        <Link
                                            :href="accounts()"
                                            :class="menuItem"
                                            >{{ $t('Settings') }}</Link
                                        >
                                    </li>
                                    <li v-if="page.props.auth.user.role">
                                        <Link
                                            :href="adminDashboard()"
                                            :class="[
                                                menuItem,
                                                'flex items-center justify-between',
                                            ]"
                                        >
                                            {{ $t('Admin') }}
                                            <span
                                                v-if="page.props.openReports"
                                                class="rounded-full bg-accent px-1.5 text-xs leading-5 font-semibold text-white tabular-nums dark:text-[#0a0a0a]"
                                                >{{ page.props.openReports
                                                }}<span class="sr-only">
                                                    {{
                                                        $t('open reports')
                                                    }}</span
                                                ></span
                                            >
                                        </Link>
                                    </li>
                                </ul>
                                <div
                                    class="border-t border-[#19140014] pt-1 dark:border-[#3E3E3A]/60"
                                >
                                    <Link
                                        :href="logout()"
                                        as="button"
                                        :class="[menuItem, 'w-full text-left']"
                                        >{{ $t('Log out') }}</Link
                                    >
                                </div>
                            </div>
                        </div>
                        <Link
                            v-else
                            :href="login()"
                            class="rounded-md border border-[#19140035] px-3 py-1.5 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                        >
                            {{ $t('Log in') }}
                        </Link>
                    </div>
                    <button
                        type="button"
                        class="relative rounded-md p-1 hover:bg-[#f4f3ef] sm:hidden dark:hover:bg-[#1c1c1a]"
                        aria-controls="mobile-menu"
                        :aria-expanded="menuOpen"
                        :aria-label="
                            menuOpen ? $t('Close menu') : $t('Open menu')
                        "
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
                :aria-label="$t('Menu')"
                class="border-t border-[#19140014] px-4 pb-4 text-sm sm:hidden dark:border-[#3E3E3A]/60"
                @keydown.esc="menuOpen = false"
            >
                <ul class="flex flex-col py-2">
                    <li>
                        <Link
                            :href="index()"
                            class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >{{ $t('Browse') }}</Link
                        >
                    </li>
                    <li>
                        <a
                            :href="create().url"
                            class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >{{ $t('Publish a mod') }}</a
                        >
                    </li>
                    <template v-if="page.props.auth.user">
                        <li>
                            <Link
                                :href="mine()"
                                class="-mx-2 block rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                                >{{ $t('My mods') }}</Link
                            >
                        </li>
                        <li v-if="page.props.auth.user.role">
                            <Link
                                :href="adminDashboard()"
                                class="-mx-2 flex items-center gap-1.5 rounded-md px-2 py-2.5 hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]"
                            >
                                {{ $t('Admin') }}
                                <span
                                    v-if="page.props.openReports"
                                    class="rounded-full bg-accent px-1.5 text-xs leading-5 font-semibold text-white tabular-nums dark:text-[#0a0a0a]"
                                    >{{ page.props.openReports
                                    }}<span class="sr-only">
                                        {{ $t('open reports') }}</span
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
                    {{ $t('Log out') }}
                </Link>
                <Link
                    v-else
                    :href="login()"
                    class="block w-full rounded-md border border-[#19140035] px-3 py-2 text-center hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                >
                    {{ $t('Log in') }}
                </Link>
            </nav>
        </header>

        <main class="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6">
            <slot />
        </main>

        <footer class="border-t border-[#19140014] dark:border-[#3E3E3A]/60">
            <div
                class="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-6 text-sm text-[#706f6c] sm:px-6 dark:text-[#A1A09A]"
            >
                <Link :href="privacy()" class="hover:underline">{{
                    $t('Privacy')
                }}</Link>
                <Link :href="terms()" class="hover:underline">{{
                    $t('Terms')
                }}</Link>
                <Link :href="policy()" class="hover:underline">{{
                    $t('Host policy')
                }}</Link>
                <Link :href="translateIndex()" class="hover:underline">{{
                    $t('Help translate')
                }}</Link>
                <button
                    v-if="page.props.analytics.google"
                    type="button"
                    class="hover:underline"
                    @click="consentOpen = true"
                >
                    {{ $t('Cookie settings') }}
                </button>

                <!-- English, and every language an admin turned on, with how much of it is done. -->
                <label class="ml-auto flex items-center gap-2">
                    <span class="sr-only">{{ $t('Language') }}</span>
                    <svg
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="1.75"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                        class="size-4"
                    >
                        <circle cx="12" cy="12" r="9" />
                        <path
                            d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"
                        />
                    </svg>
                    <select
                        :value="page.props.locale"
                        class="rounded-md border border-[#19140035] bg-transparent px-2 py-1 text-sm dark:border-[#3E3E3A]"
                        @change="
                            router.post(
                                switchLocale().url,
                                {
                                    locale: ($event.target as HTMLSelectElement)
                                        .value,
                                },
                                { preserveScroll: true },
                            )
                        "
                    >
                        <option value="en">English</option>
                        <option
                            v-for="language in page.props.languages"
                            :key="language.code"
                            :value="language.code"
                        >
                            {{ language.native_name }}
                            <template v-if="language.progress < 100"
                                >({{ language.progress }}%)</template
                            >
                        </option>
                    </select>
                </label>
            </div>
        </footer>

        <CookieConsent />

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
                    v-if="notificationExcerpt(toast)"
                    class="mt-0.5 line-clamp-2 break-words text-[#706f6c] dark:text-[#A1A09A]"
                >
                    {{ notificationExcerpt(toast) }}
                </p>
            </a>
        </div>
    </div>
</template>
