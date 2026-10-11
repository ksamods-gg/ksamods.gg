<script setup lang="ts">
import { Head, router, usePage } from '@inertiajs/vue3';
import { ref } from 'vue';
import AdminNav from '@/components/admin/AdminNav.vue';
import Pager from '@/components/admin/Pager.vue';
import ModIcon from '@/components/ModIcon.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate, formatNumber } from '@/lib/format';
import { users as usersPage } from '@/routes/admin';
import { role as roleRoute } from '@/routes/admin/users';
import type { AdminUser, Paginated } from '@/types';

const props = defineProps<{
    search: string;
    canChangeRoles: boolean;
    users: Paginated<AdminUser>;
}>();

const page = usePage();
const query = ref(props.search);

function apply() {
    router.get(
        usersPage().url,
        { q: query.value || undefined },
        { preserveState: true },
    );
}

function changeRole(user: AdminUser, event: Event) {
    const select = event.target as HTMLSelectElement;
    const role = (select.value || null) as AdminUser['role'];
    const label = role
        ? { admin: 'an admin', moderator: 'a moderator' }[role]
        : 'a regular user';

    if (!confirm(`Make ${user.name} ${label}?`)) {
        select.value = user.role ?? '';

        return;
    }

    router.patch(roleRoute(user.id).url, { role }, { preserveScroll: true });
}

const roleNames = { admin: 'Admin', moderator: 'Moderator' };

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const control =
    'rounded-md border border-[#19140035] bg-white px-2 py-1 text-sm outline-none focus:border-[#1b1b18] disabled:opacity-60 dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
</script>

<template>
    <Head title="Users" />

    <AppLayout>
        <AdminNav />

        <form role="search" class="mb-4" @submit.prevent="apply">
            <label>
                <span class="sr-only">Search users</span>
                <input
                    v-model="query"
                    type="search"
                    placeholder="Search by name, GitHub login or Discord username"
                    :class="[control, 'w-full px-3 py-2 sm:max-w-md']"
                />
            </label>
        </form>

        <div
            v-if="users.data.length"
            class="overflow-x-auto rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
        >
            <table class="w-full min-w-[40rem] text-left text-sm">
                <thead :class="['text-xs', muted]">
                    <tr
                        class="shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_#3E3E3A]"
                    >
                        <th scope="col" class="px-4 py-3 font-medium">User</th>
                        <th scope="col" class="px-4 py-3 font-medium">
                            Accounts
                        </th>
                        <th scope="col" class="px-4 py-3 font-medium">Posts</th>
                        <th scope="col" class="px-4 py-3 font-medium">
                            Joined
                        </th>
                        <th scope="col" class="px-4 py-3 font-medium">Role</th>
                    </tr>
                </thead>
                <tbody
                    class="divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                >
                    <tr v-for="user in users.data" :key="user.id">
                        <td class="px-4 py-3">
                            <div class="flex items-center gap-2">
                                <ModIcon
                                    :name="user.name"
                                    :src="user.avatar"
                                    class="size-7 shrink-0 rounded-full text-xs"
                                />
                                <span class="font-medium">{{ user.name }}</span>
                            </div>
                        </td>
                        <td :class="['px-4 py-3', muted]">
                            <div v-if="user.github_login">
                                GitHub
                                <a
                                    :href="`https://github.com/${user.github_login}`"
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    class="text-[#1b1b18] hover:underline dark:text-[#EDEDEC]"
                                    >{{ user.github_login }}</a
                                >
                            </div>
                            <div v-if="user.discord_username">
                                Discord
                                <span
                                    class="text-[#1b1b18] dark:text-[#EDEDEC]"
                                    >{{ user.discord_username }}</span
                                >
                            </div>
                        </td>
                        <td :class="['px-4 py-3 tabular-nums', muted]">
                            {{ formatNumber(user.reviews) }} reviews,
                            {{ formatNumber(user.comments) }} comments
                        </td>
                        <td :class="['px-4 py-3', muted]">
                            <time :datetime="user.created_at">{{
                                formatDate(user.created_at)
                            }}</time>
                        </td>
                        <td class="px-4 py-3">
                            <!-- Admins change roles, but never their own. -->
                            <select
                                v-if="
                                    canChangeRoles &&
                                    user.id !== page.props.auth.user?.id
                                "
                                :value="user.role ?? ''"
                                :aria-label="`Role of ${user.name}`"
                                :class="control"
                                @change="changeRole(user, $event)"
                            >
                                <option value="">None</option>
                                <option value="moderator">Moderator</option>
                                <option value="admin">Admin</option>
                            </select>
                            <span v-else-if="user.role">{{
                                roleNames[user.role]
                            }}</span>
                            <span v-else :class="muted">None</span>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>
        <p
            v-else
            :class="[
                'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            {{ search ? `No users match “${search}”.` : 'No users yet.' }}
        </p>

        <Pager :page="users" />
    </AppLayout>
</template>
