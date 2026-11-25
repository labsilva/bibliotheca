(() => {
  const parameters = new URLSearchParams(window.location.hash.slice(1))
  if (!parameters.has('inviteCode')) return
  const input = document.getElementById('invitecode')
  if (input) input.value = parameters.get('inviteCode') || input.value
  // Fragments stay in the browser; remove the invitation from visible history too.
  window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search)
})()
