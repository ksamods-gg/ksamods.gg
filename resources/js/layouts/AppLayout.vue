<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import { home, login, logout } from '@/routes';
import { create } from '@/routes/listings';
import { mine } from '@/routes/mods';

const page = usePage();
</script>

<template>
    <div
        class="min-h-screen bg-[#FDFDFC] text-[#1b1b18] dark:bg-[#0a0a0a] dark:text-[#EDEDEC]"
    >
        <header class="border-b border-[#19140014] dark:border-[#3E3E3A]/60">
            <div
                class="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-3 sm:px-6"
            >
                <Link :href="home()" class="font-semibold tracking-tight">
                    {{ page.props.name }}
                </Link>

                <!-- On the narrowest phones this wraps below the site name rather than overflowing. -->
                <div
                    class="ml-auto flex flex-wrap items-center justify-end gap-x-3 gap-y-2 text-sm"
                >
                    <!-- A plain link: guests get redirected to GitHub to log in. -->
                    <a :href="create().url" class="hover:underline">
                        Submit a mod
                    </a>
                    <template v-if="page.props.auth.user">
                        <Link :href="mine()" class="hover:underline">
                            My mods
                        </Link>
                        <img
                            v-if="page.props.auth.user.avatar"
                            :src="page.props.auth.user.avatar"
                            alt=""
                            class="hidden size-7 rounded-full sm:block"
                        />
                        <span class="hidden sm:inline">{{
                            page.props.auth.user.name
                        }}</span>
                        <Link
                            :href="logout()"
                            as="button"
                            class="rounded-md border border-[#19140035] px-3 py-1.5 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                        >
                            Log out
                        </Link>
                    </template>
                    <a
                        v-else
                        :href="login().url"
                        class="rounded-md border border-[#19140035] px-3 py-1.5 hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                    >
                        Log in<span class="hidden sm:inline"> with GitHub</span>
                    </a>
                </div>
            </div>
        </header>

        <main class="mx-auto max-w-6xl px-4 py-8 sm:px-6">
            <slot />
        </main>
    </div>
</template>
