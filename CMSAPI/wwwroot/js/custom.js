(function () {
    window.addEventListener('load', function () {
        var topBar = document.querySelector('.topbar-wrapper .link');
        if (topBar) {
            var timeDisplay = document.createElement('span');
            timeDisplay.style.marginLeft = '20px';
            timeDisplay.style.color = 'white';
            timeDisplay.style.fontWeight = 'bold';
            timeDisplay.style.fontSize = '14px';

            // Insert after the logo
            topBar.appendChild(timeDisplay);

            function updateTime() {
                var now = new Date();
                timeDisplay.innerText = now.toLocaleString();
            }

            setInterval(updateTime, 1000);
            updateTime();
        }
    });
})();
