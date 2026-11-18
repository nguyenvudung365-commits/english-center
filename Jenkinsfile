pipeline {
    agent { label 'dotnet10-docker' }
    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        timeout(time: 40, unit: 'MINUTES')
    }
    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
    }
    stages {
        stage('Lấy mã nguồn') {
            steps {
                checkout scm
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse HEAD', returnStdout: true).trim()
                }
            }
        }
        stage('Khôi phục và biên dịch') {
            steps {
                sh 'dotnet restore EnglishCenter.sln'
                sh 'dotnet build EnglishCenter.sln -c Release --no-restore'
            }
        }
        stage('Kiểm thử') {
            steps {
                sh 'bash tests/scripts/ci-postgres-tests.sh'
                sh 'bash tests/scripts/ci-smoke-tests.sh'
                sh 'bash tests/scripts/compose-storage-tests.sh'
                sh 'bash tests/scripts/deploy-tests.sh'
                sh 'dotnet test EnglishCenter.sln -c Release --no-build --logger trx --results-directory TestResults'
            }
        }
        stage('Identity và Media trên PostgreSQL thật') {
            steps { sh 'bash scripts/ci-postgres.sh' }
        }
        stage('Kiểm tra container và PostgreSQL') {
            steps { sh 'bash scripts/ci-smoke.sh' }
        }
        stage('Build frontend') {
            steps {
                sh 'docker build -t "english-center-web:$IMAGE_TAG" frontend'
            }
        }
        stage('Build image bàn giao') {
            when { branch 'main' }
            steps {
                sh 'docker build -t "english-center-api:$IMAGE_TAG" .'
            }
        }
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
                sh '''
                    test "$(git rev-parse HEAD)" = "$IMAGE_TAG"
                    docker build -t "english-center-api:$IMAGE_TAG" .
                    docker build -t "english-center-web:$IMAGE_TAG" frontend
                '''
                withCredentials([file(credentialsId: 'english-center-deploy-env', variable: 'DEPLOY_ENV')]) {
                    sh 'set +x; bash scripts/deploy.sh'
                }
            }
        }
    }
    post {
        always {
            archiveArtifacts artifacts: 'TestResults/**/*.trx', allowEmptyArchive: true
        }
    }
}
