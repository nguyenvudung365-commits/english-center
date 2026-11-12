pipeline {
    agent { label 'dotnet10-docker' }
    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        timeout(time: 40, unit: 'MINUTES')
    }
    parameters {
        booleanParam(name: 'DEPLOY', defaultValue: false, description: 'Triển khai R2 lên Docker host của agent, chỉ main; không tự SSH tới VPS.')
    }
    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
    }
    stages {
        stage('Lấy mã nguồn') {
            steps { checkout scm }
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
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse HEAD', returnStdout: true).trim()
                }
                sh 'docker build -t "english-center-web:$IMAGE_TAG" frontend'
            }
        }
        stage('Build image bàn giao') {
            when { branch 'main' }
            steps {
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse HEAD', returnStdout: true).trim()
                }
                sh 'docker build -t "english-center-api:$IMAGE_TAG" .'
            }
        }
        stage('Triển khai') {
            when { allOf { branch 'main'; expression { params.DEPLOY } } }
            steps {
                // Chỉ job main đáng tin cậy được cấp credential R2 theo .env.example.
                withCredentials([file(credentialsId: 'english-center-deploy-env', variable: 'DEPLOY_ENV')]) {
                    sh '''
                        set +x
                        docker compose --env-file "$DEPLOY_ENV" --project-name english-center -f compose.yaml -f compose.web.yaml config --quiet
                        docker compose --env-file "$DEPLOY_ENV" --project-name english-center -f compose.yaml -f compose.web.yaml up -d --no-build --wait --wait-timeout 180
                    '''
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
