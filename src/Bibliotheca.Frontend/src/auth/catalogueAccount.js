import { shallowRef } from 'vue'

export const catalogueAccount = shallowRef(undefined)

export function catalogueAccountKey(user, authority) {
  const subject = user?.profile?.sub
  return subject ? JSON.stringify([user.profile.iss ?? authority, subject]) : null
}
