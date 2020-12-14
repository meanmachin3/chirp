$(() => {
  $('#follow').on('click', () => {
    const $this = $(event.currentTarget)
    const userId = $this.data('user-id')
    $this.prop('disabled', true)
    $.ajax({
      url : '/follow',
      type: 'post',
      data: JSON.stringify({userId}),
      contentType: 'application/json'
    }).done(() => {
      alert('Successfully followed')
      $this.prop('disabled', false)
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({jqXHR, textStatus, errorThrown})
      alert('Something went wrong!')
    })
  })

  const usersTemplate =
    `{{#users}}
      <div class="well user-card">
        <a href="/{{username}}">@{{username}}</a>
      </div>
    {{/users}}
    `

  const renderUsers = (data, $body, $count) => {
    const htmlOutput = Mustache.render(usersTemplate, data)
    $body.html(htmlOutput)
    $count.html(data.users.length)
  }

  const loadFollowers = () => {
    const url = `/${fsTweet.user.id}/followers`
    $.getJSON(url, data => renderUsers(data, $('#followers'), $('#followersCount')))
  }

  const loadFollowees = () => {
    const url = `/${fsTweet.user.id}/followees`
    $.getJSON(url, data => renderUsers(data, $('#following'), $('#followingCount')))
  }

  loadFollowers()
  loadFollowees()
})
