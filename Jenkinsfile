pipeline {
    agent none
    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        timeout(time: 85, unit: 'MINUTES')
    }
    stages {
        stage('Tự động triển khai VPS') {
            when {
                beforeAgent true
                allOf {
                    branch 'main'
                    not { buildingTag() }
                    not { changeRequest() }
                    expression { env.ENGLISH_CENTER_AUTO_DEPLOY == 'true' }
                }
            }
            agent { label 'english-center-production' }
            steps {
                deleteDir()
                checkout scm
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse HEAD', returnStdout: true).trim()
                    withCredentials([string(credentialsId: 'english-center-actions-read', variable: 'GH_TOKEN')]) {
                        def images = sh(script: 'set +x; bash scripts/verified-release.sh', returnStdout: true).trim().readLines()
                        if (images.size() != 2) {
                            error('Thiếu cặp image API/web đã xác minh.')
                        }
                        env.ENGLISH_CENTER_API_IMAGE = images[0]
                        env.ENGLISH_CENTER_WEB_IMAGE = images[1]
                    }
                }
                withCredentials([file(credentialsId: 'english-center-deploy-env', variable: 'DEPLOY_ENV')]) {
                    sh 'set +x; bash scripts/deploy.sh'
                }
            }
        }
    }
}
